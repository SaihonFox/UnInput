using System.Threading.Channels;
using UniversalInput.Core.Diagnostics;
using UniversalInput.Core.Events;

namespace UniversalInput.Core.Subscriptions;

/// <summary>
/// Подписка со своей ограниченной очередью (кольцевой буфер). Медленный потребитель не задерживает остальных.
/// При заполнении: непрерывные события склеиваются, дискретные обрабатываются политикой <see cref="OverflowPolicy"/>,
/// а потеря всегда отражается событием StreamGap.
/// </summary>
internal sealed class Subscription : IInputSubscription
{
	private readonly object _gate = new();
	private readonly InputEvent[] _ring;
	private readonly EventFilter _filter;
	private readonly OverflowPolicy _policy;
	private readonly InputMetrics _metrics;
	private readonly Action<Subscription> _onDispose;
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly SubscriptionReader _reader;
	private readonly TimeProvider _time;
	private readonly TimeSpan _rateInterval;
	private readonly Dictionary<RateKey, RateStream> _streams = new();

	private ITimer? _rateTimer;
	private int _head;
	private int _count;
	private long _dropped;
	private bool _completed;
	private Exception? _fault;
	private TaskCompletionSource<bool>? _waiter;

	public Subscription(
		SubscriptionOptions options,
		EventFilter filter,
		InputMetrics metrics,
		Action<Subscription> onDispose,
		TimeProvider? time = null)
	{
		if (options.Capacity < 2)
		{
			throw new ArgumentOutOfRangeException(nameof(options), options.Capacity, "Ёмкость подписки должна быть не меньше 2.");
		}

		_time = time ?? TimeProvider.System;
		if (options.MaxContinuousRate is { } rate)
		{
			if (!(rate > 0) || double.IsInfinity(rate))
			{
				throw new ArgumentOutOfRangeException(
					nameof(options), rate, "MaxContinuousRate должен быть положительным конечным числом.");
			}

			// Не короче одного такта и не длиннее года: при меньших частотах предел всё равно бессмыслен.
			var ticks = Math.Ceiling(TimeSpan.TicksPerSecond / rate);
			_rateInterval = TimeSpan.FromTicks((long)Math.Clamp(ticks, 1d, (double)(TimeSpan.TicksPerDay * 365)));
		}

		_ring = new InputEvent[options.Capacity];
		_policy = options.Overflow;
		_filter = filter;
		_metrics = metrics;
		_onDispose = onDispose;
		_reader = new SubscriptionReader(this);
	}

	public ChannelReader<InputEvent> Reader => _reader;

	public long DroppedCount => Interlocked.Read(ref _dropped);

	/// <summary>Предлагает событие подписке. Вызывается только диспетчером.</summary>
	public void Offer(in InputEvent inputEvent, InputDevice? device, bool aggregateDuplicate = false)
	{
		if (!_filter.Matches(in inputEvent, device, aggregateDuplicate))
		{
			return;
		}

		TaskCompletionSource<bool>? toWake;
		lock (_gate)
		{
			if (_completed)
			{
				return;
			}

			if (_rateInterval > TimeSpan.Zero)
			{
				AdmitLimited(in inputEvent);
			}
			else
			{
				Enqueue(in inputEvent);
			}

			toWake = TakeWaiter();
		}

		toWake?.TrySetResult(true);
	}

	/// <summary>Завершает подписку: читатель получит оставшиеся события, затем завершение (или ошибку).</summary>
	public void Complete(Exception? fault = null)
	{
		TaskCompletionSource<bool>? waiter;
		bool empty;
		lock (_gate)
		{
			if (!_completed)
			{
				// Накопленные из-за предела частоты события не должны пропасть: последнее значение — самое важное.
				FlushRate(DeviceId.None);
				_completed = true;
				_fault ??= fault;
			}

			_rateTimer?.Dispose();
			_rateTimer = null;

			waiter = _waiter;
			_waiter = null;
			empty = _count == 0;
			if (empty)
			{
				SignalCompletion();
			}
		}

		if (waiter is not null)
		{
			if (!empty)
			{
				waiter.TrySetResult(true);
			}
			else if (_fault is not null)
			{
				waiter.TrySetException(_fault);
			}
			else
			{
				waiter.TrySetResult(false);
			}
		}
	}

	public ValueTask DisposeAsync()
	{
		_onDispose(this);
		Complete();
		return ValueTask.CompletedTask;
	}

	private bool TryRead(out InputEvent item)
	{
		lock (_gate)
		{
			if (_count == 0)
			{
				item = default;
				if (_completed)
				{
					SignalCompletion();
				}

				return false;
			}

			item = _ring[_head];
			_head = (_head + 1) % _ring.Length;
			_count--;

			if (_count == 0 && _completed)
			{
				SignalCompletion();
			}

			return true;
		}
	}

	private ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource<bool> waiter;
		lock (_gate)
		{
			if (_count > 0)
			{
				return new ValueTask<bool>(true);
			}

			if (_completed)
			{
				SignalCompletion();
				return _fault is not null
					? ValueTask.FromException<bool>(_fault)
					: new ValueTask<bool>(false);
			}

			if (cancellationToken.IsCancellationRequested)
			{
				return ValueTask.FromCanceled<bool>(cancellationToken);
			}

			_waiter ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			waiter = _waiter;
		}

		return cancellationToken.CanBeCanceled
			? new ValueTask<bool>(waiter.Task.WaitAsync(cancellationToken))
			: new ValueTask<bool>(waiter.Task);
	}

	// Ниже — работа с буфером; вызывается под _gate.

	private void SignalCompletion()
	{
		if (_fault is not null)
		{
			_completion.TrySetException(_fault);
		}
		else
		{
			_completion.TrySetResult();
		}
	}

	private void Append(in InputEvent inputEvent)
	{
		_ring[(_head + _count) % _ring.Length] = inputEvent;
		_count++;
	}

	private void Enqueue(in InputEvent inputEvent)
	{
		if (_count < _ring.Length)
		{
			Append(in inputEvent);
		}
		else
		{
			HandleFull(in inputEvent);
		}
	}

	private TaskCompletionSource<bool>? TakeWaiter()
	{
		if (_count > 0 && _waiter is not null)
		{
			var waiter = _waiter;
			_waiter = null;
			return waiter;
		}

		return null;
	}

	// Предел частоты (MaxContinuousRate). Всё ниже вызывается под _gate.

	/// <summary>
	/// Непрерывное событие проходит сразу, если с прошлого прошло не меньше интервала; иначе оно ждёт в «отложенном»
	/// слоте своего потока и склеивается со следующими, а по таймеру уходит одним событием. Дискретное событие
	/// сначала выпускает отложенные события своего устройства, чтобы порядок не нарушился.
	/// </summary>
	private void AdmitLimited(in InputEvent inputEvent)
	{
		if (!inputEvent.IsContinuous)
		{
			FlushRate(inputEvent.Device);
			if (inputEvent.Kind == InputEventKind.DeviceDisconnected)
			{
				ForgetStreams(inputEvent.Device);
			}

			Enqueue(in inputEvent);
			return;
		}

		var key = KeyOf(in inputEvent);
		var now = _time.GetTimestamp();

		if (!_streams.TryGetValue(key, out var stream))
		{
			stream = new RateStream();
			_streams[key] = stream;
		}
		else if (stream.HasPending)
		{
			if (EventCoalescer.TryMerge(in stream.Pending, in inputEvent, out var merged))
			{
				stream.Pending = merged;
				_metrics.Coalesced();
				return;
			}

			// Ключ учитывает всё, что мешает склейке, поэтому сюда попадают только непредвиденные случаи.
			EmitPending(stream, now);
			stream.Pending = inputEvent;
			stream.HasPending = true;
			ScheduleRateTimer(now);
			return;
		}
		else if (_time.GetElapsedTime(stream.LastEmit, now) < _rateInterval)
		{
			stream.Pending = inputEvent;
			stream.HasPending = true;
			ScheduleRateTimer(now);
			return;
		}

		stream.LastEmit = now;
		Enqueue(in inputEvent);
	}

	private static RateKey KeyOf(in InputEvent inputEvent)
	{
		var isChannelled = inputEvent.Kind is InputEventKind.GamepadAxisChanged
			or InputEventKind.TouchMoved
			or InputEventKind.SensorChanged;
		var channel = isChannelled ? inputEvent.Int0 : 0;

		return new RateKey(inputEvent.Device, inputEvent.Kind, channel, (inputEvent.Flags & InputEventFlags.Absolute) != 0);
	}

	private void EmitPending(RateStream stream, long now)
	{
		stream.HasPending = false;
		stream.LastEmit = now;
		Enqueue(in stream.Pending);
	}

	/// <summary>Выпускает отложенные события устройства (для <see cref="DeviceId.None"/> — всех устройств).</summary>
	private void FlushRate(DeviceId device)
	{
		if (_streams.Count == 0)
		{
			return;
		}

		var now = _time.GetTimestamp();
		foreach (var pair in _streams)
		{
			if (pair.Value.HasPending && (device.IsNone || pair.Key.Device == device))
			{
				EmitPending(pair.Value, now);
			}
		}
	}

	private void ForgetStreams(DeviceId device)
	{
		List<RateKey>? stale = null;
		foreach (var key in _streams.Keys)
		{
			if (key.Device == device)
			{
				stale ??= [];
				stale.Add(key);
			}
		}

		if (stale is null)
		{
			return;
		}

		foreach (var key in stale)
		{
			_streams.Remove(key);
		}
	}

	private void ScheduleRateTimer(long now)
	{
		var delay = TimeSpan.MaxValue;
		foreach (var stream in _streams.Values)
		{
			if (!stream.HasPending)
			{
				continue;
			}

			var remaining = _rateInterval - _time.GetElapsedTime(stream.LastEmit, now);
			if (remaining < delay)
			{
				delay = remaining;
			}
		}

		if (delay == TimeSpan.MaxValue)
		{
			return;
		}

		if (delay < TimeSpan.Zero)
		{
			delay = TimeSpan.Zero;
		}

		if (_rateTimer is null)
		{
			_rateTimer = _time.CreateTimer(OnRateTimer, null, delay, Timeout.InfiniteTimeSpan);
		}
		else
		{
			_rateTimer.Change(delay, Timeout.InfiniteTimeSpan);
		}
	}

	private void OnRateTimer(object? state)
	{
		TaskCompletionSource<bool>? toWake;
		lock (_gate)
		{
			if (_completed)
			{
				return;
			}

			var now = _time.GetTimestamp();
			foreach (var stream in _streams.Values)
			{
				if (stream.HasPending && _time.GetElapsedTime(stream.LastEmit, now) >= _rateInterval)
				{
					EmitPending(stream, now);
				}
			}

			ScheduleRateTimer(now);
			toWake = TakeWaiter();
		}

		toWake?.TrySetResult(true);
	}

	private void HandleFull(in InputEvent inputEvent)
	{
		if (inputEvent.IsContinuous && TryCoalesce(in inputEvent))
		{
			_metrics.Coalesced();
			return;
		}

		switch (_policy)
		{
			case OverflowPolicy.DropOldest:
				DropOldestPair();
				Append(in inputEvent);
				break;

			case OverflowPolicy.DropNewest:
				DropNewest();
				break;

			default:
				_completed = true;
				_fault = new InvalidOperationException(
					"Очередь подписки переполнена, а выбрана политика FaultSubscription.");
				Interlocked.Increment(ref _dropped);
				_metrics.Dropped("subscription", "fault");
				break;
		}
	}

	/// <summary>Склеивает событие с последним подходящим в очереди. Просмотр обрывается на дискретном событии того же устройства: порядок не нарушается.</summary>
	private bool TryCoalesce(in InputEvent inputEvent)
	{
		for (var i = _count - 1; i >= 0; i--)
		{
			var index = (_head + i) % _ring.Length;
			ref var queued = ref _ring[index];

			if (queued.Device != inputEvent.Device)
			{
				continue;
			}

			if (EventCoalescer.TryMerge(in queued, in inputEvent, out var merged))
			{
				queued = merged;
				return true;
			}

			if (!queued.IsContinuous)
			{
				return false;
			}
		}

		return false;
	}

	/// <summary>Два самых старых события заменяются одним StreamGap: так освобождается место для нового события без потери сведений о пропуске.</summary>
	private void DropOldestPair()
	{
		var first = _ring[_head];
		var nextIndex = (_head + 1) % _ring.Length;
		var second = _ring[nextIndex];

		var lost = Add(Weight(first), Weight(second));
		var realLost = (first.Kind == InputEventKind.StreamGap ? 0 : 1) + (second.Kind == InputEventKind.StreamGap ? 0 : 1);

		_ring[nextIndex] = Gap(second, lost, StreamGapReason.SubscriptionOverflow);
		_head = nextIndex;
		_count--;

		Interlocked.Add(ref _dropped, realLost);
		_metrics.Dropped("subscription", "overflow", realLost);
	}

	/// <summary>Новое событие отбрасывается; чтобы пропуск был виден, последнее событие в очереди заменяется на StreamGap (потеряны оба).</summary>
	private void DropNewest()
	{
		var tailIndex = (_head + _count - 1) % _ring.Length;
		var tail = _ring[tailIndex];

		if (tail.Kind == InputEventKind.StreamGap && tail.AsStreamGap().Reason == StreamGapReason.SubscriptionDroppedNewest)
		{
			_ring[tailIndex] = Gap(tail, Add(Weight(tail), 1), StreamGapReason.SubscriptionDroppedNewest);
			Interlocked.Increment(ref _dropped);
			_metrics.Dropped("subscription", "dropped_newest");
			return;
		}

		var realLost = 1 + (tail.Kind == InputEventKind.StreamGap ? 0 : 1);
		_ring[tailIndex] = Gap(tail, Add(Weight(tail), 1), StreamGapReason.SubscriptionDroppedNewest);
		Interlocked.Add(ref _dropped, realLost);
		_metrics.Dropped("subscription", "dropped_newest", realLost);
	}

	private static int Add(int a, int b) => (int)Math.Min(int.MaxValue, (long)a + b);

	private static int Weight(in InputEvent inputEvent) =>
		inputEvent.Kind == InputEventKind.StreamGap ? inputEvent.Int0 : 1;

	private static InputEvent Gap(in InputEvent basis, int lost, StreamGapReason reason) =>
		InputEvent.Gap(lost, reason, basis.Timestamp) with
		{
			Sequence = basis.Sequence,
			Backend = basis.Backend,
		};

	/// <summary>Поток непрерывных событий, к которому применяется предел частоты.</summary>
	private readonly record struct RateKey(DeviceId Device, InputEventKind Kind, int Channel, bool Absolute);

	private sealed class RateStream
	{
		public long LastEmit;
		public bool HasPending;
		public InputEvent Pending;
	}

	private sealed class SubscriptionReader : ChannelReader<InputEvent>
	{
		private readonly Subscription _owner;

		public SubscriptionReader(Subscription owner) => _owner = owner;

		public override Task Completion => _owner._completion.Task;

		public override bool TryRead(out InputEvent item) => _owner.TryRead(out item);

		public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) =>
			_owner.WaitToReadAsync(cancellationToken);
	}
}

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
		Action<Subscription> onDispose)
	{
		if (options.Capacity < 2)
		{
			throw new ArgumentOutOfRangeException(nameof(options), options.Capacity, "Ёмкость подписки должна быть не меньше 2.");
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
	public void Offer(in InputEvent inputEvent, InputDevice? device)
	{
		if (!_filter.Matches(in inputEvent, device))
		{
			return;
		}

		TaskCompletionSource<bool>? toWake = null;
		lock (_gate)
		{
			if (_completed)
			{
				return;
			}

			if (_count < _ring.Length)
			{
				Append(in inputEvent);
			}
			else
			{
				HandleFull(in inputEvent);
			}

			if (_count > 0 && _waiter is not null)
			{
				toWake = _waiter;
				_waiter = null;
			}
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
				_completed = true;
				_fault ??= fault;
			}

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

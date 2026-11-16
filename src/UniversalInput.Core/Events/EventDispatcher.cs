using System.Threading.Channels;
using UniversalInput.Core.Backends;
using UniversalInput.Core.Devices;
using UniversalInput.Core.Diagnostics;
using UniversalInput.Core.Subscriptions;

namespace UniversalInput.Core.Events;

/// <summary>
/// Единственный диспетчер: читает очереди всех backend, присваивает <see cref="InputEvent.Sequence"/>,
/// отбрасывает события неизвестных устройств и раздаёт события подпискам.
/// Порядок сохраняется внутри очереди одного backend; между backend порядок — по времени прибытия в диспетчер.
/// </summary>
internal sealed class EventDispatcher
{
	private const int BatchPerQueue = 256;

	private readonly BackendQueue[] _queues;
	private readonly DeviceRegistry _registry;
	private readonly InputMetrics _metrics;
	private readonly IReadOnlyDictionary<string, ushort> _backendIndexes;
	private readonly TimeProvider _time;
	private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
	{
		SingleReader = true,
		SingleWriter = false,
		FullMode = BoundedChannelFullMode.DropWrite,
	});

	private readonly object _subscriptionsGate = new();
	private readonly CancellationTokenSource _cts = new();
	private Subscription[] _subscriptions = [];
	private Task? _loop;
	private long _sequence;
	private bool _stopped;

	public EventDispatcher(
		BackendQueue[] queues,
		DeviceRegistry registry,
		InputMetrics metrics,
		IReadOnlyDictionary<string, ushort> backendIndexes,
		TimeProvider? time = null)
	{
		_queues = queues;
		_registry = registry;
		_metrics = metrics;
		_backendIndexes = backendIndexes;
		_time = time ?? TimeProvider.System;
	}

	/// <summary>Сколько событий уже обработано (последний присвоенный номер).</summary>
	public long LastSequence => Interlocked.Read(ref _sequence);

	/// <summary>Будит диспетчер. Вызывается после каждой записи в очередь backend.</summary>
	public void Wake() => _wake.Writer.TryWrite(true);

	public void Start()
	{
		_loop = Task.Run(() => RunAsync(_cts.Token));
	}

	/// <summary>Останавливает цикл, выполняет последнюю выборку очередей (чтобы подписчики получили отключения устройств) и завершает подписки.</summary>
	public async Task StopAsync()
	{
		_cts.Cancel();
		if (_loop is not null)
		{
			await _loop.ConfigureAwait(false);
		}

		DrainAll();

		Subscription[] remaining;
		lock (_subscriptionsGate)
		{
			_stopped = true;
			remaining = _subscriptions;
			_subscriptions = [];
		}

		foreach (var subscription in remaining)
		{
			subscription.Complete();
		}
	}

	public IInputSubscription Subscribe(SubscriptionOptions options)
	{
		var filter = EventFilter.Create(options, _backendIndexes);
		var subscription = new Subscription(options, filter, _metrics, Unsubscribe, _time);

		lock (_subscriptionsGate)
		{
			if (_stopped)
			{
				subscription.Complete();
				return subscription;
			}

			_subscriptions = [.. _subscriptions, subscription];
		}

		return subscription;
	}

	private void Unsubscribe(Subscription subscription)
	{
		lock (_subscriptionsGate)
		{
			_subscriptions = _subscriptions.Where(s => !ReferenceEquals(s, subscription)).ToArray();
		}
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (await _wake.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
			{
				_wake.Reader.TryRead(out _);
				DrainAll();
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	/// <summary>Выбирает очереди по кругу, пока в них что-то есть; по BatchPerQueue событий за заход, чтобы один backend не заглушал остальных.</summary>
	private void DrainAll()
	{
		bool any;
		do
		{
			any = false;
			foreach (var queue in _queues)
			{
				var drops = queue.TakeDrops();

				var taken = 0;
				while (taken < BatchPerQueue && queue.TryRead(out var inputEvent))
				{
					Dispatch(in inputEvent, queue);
					taken++;
					any = true;
				}

				if (drops > 0)
				{
					_metrics.Dropped(queue.BackendId, "backend_queue_full", drops);
					var gap = InputEvent.Gap((int)Math.Min(drops, int.MaxValue), StreamGapReason.BackendQueueOverflow);
					Dispatch(in gap, queue);
					any = true;
				}
			}
		}
		while (any);
	}

	private void Dispatch(in InputEvent source, BackendQueue queue)
	{
		var inputEvent = source with
		{
			Sequence = ++_sequence,
			Backend = queue.Index,
		};

		InputDevice? device = null;
		var aggregateDuplicate = false;
		if (inputEvent.Kind != InputEventKind.StreamGap && !inputEvent.Device.IsNone)
		{
			if (!_registry.TryGet(inputEvent.Device, out var entry) || !entry.WasProvider(queue.Index))
			{
				_metrics.Dropped(queue.BackendId, "unknown_device");
				return;
			}

			var lifecycle = inputEvent.Kind is InputEventKind.DeviceConnected or InputEventKind.DeviceDisconnected;
			if (!entry.Live && !lifecycle)
			{
				_metrics.Dropped(queue.BackendId, "removed_device");
				return;
			}

			// Роль Observe у логического устройства одна: события остальных поставщиков были бы дублями.
			var state = entry.State;
			if (!lifecycle && state.ObserveOwner != queue.Index)
			{
				_metrics.Dropped(queue.BackendId, "not_role_owner");
				return;
			}

			device = state.Device;
			if (device.Origin == DeviceOrigin.Aggregate)
			{
				inputEvent = inputEvent with { Flags = inputEvent.Flags | InputEventFlags.Aggregate };
				aggregateDuplicate = IsObservedPerDevice(inputEvent.Kind);
			}
		}

		_metrics.Dispatched(queue.BackendId);

		foreach (var subscription in Volatile.Read(ref _subscriptions))
		{
			subscription.Offer(in inputEvent, device, aggregateDuplicate);
		}
	}

	/// <summary>
	/// Те же события приходят и от агрегата (глобальный хук), и от устройств по отдельности. Пока устройства
	/// этой категории наблюдаются, агрегатные события — дубли; подписка без явного выбора устройств их не получает.
	/// </summary>
	private bool IsObservedPerDevice(InputEventKind kind) => kind switch
	{
		InputEventKind.KeyPressed or InputEventKind.KeyReleased => _registry.KeyboardObserved,
		InputEventKind.MouseMoved
			or InputEventKind.MouseButtonPressed
			or InputEventKind.MouseButtonReleased
			or InputEventKind.MouseWheel => _registry.MouseObserved,
		_ => false,
	};
}

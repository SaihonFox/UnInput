using UniversalInput.Core.Backends;
using UniversalInput.Core.Devices;
using UniversalInput.Core.Diagnostics;
using UniversalInput.Core.Events;
using UniversalInput.Testing.Mock;

namespace UniversalInput.Core.Tests.Events;

/// <summary>
/// Диспетчер без фонового цикла: <see cref="EventDispatcher.StopAsync"/> выбирает очереди синхронно, поэтому порядок детерминирован.
/// </summary>
public class EventDispatcherTests
{
	private readonly InputMetrics _metrics = new();
	private readonly DeviceRegistry _registry = new(TimeProvider.System, TimeSpan.FromMinutes(5));

	private DeviceId AddDevice(ushort backend, string key, DeviceOrigin origin = DeviceOrigin.Physical) =>
		_registry.Add(backend, $"b{backend}", MockInputBackend.Describe(key, InputDeviceKind.Keyboard, origin: origin)).Id;

	private EventDispatcher Create(params BackendQueue[] queues)
	{
		var indexes = queues.ToDictionary(q => q.BackendId, q => q.Index);
		return new EventDispatcher(queues, _registry, _metrics, indexes);
	}

	private static async Task<List<InputEvent>> ReadAll(IInputSubscription subscription)
	{
		List<InputEvent> events = [];
		await foreach (var inputEvent in subscription.Reader.ReadAllAsync())
		{
			events.Add(inputEvent);
		}

		return events;
	}

	[Fact]
	public async Task Dispatch_AssignsIncreasingSequenceAndBackendIndex()
	{
		var device = AddDevice(0, "k");
		var queue = new BackendQueue(0, "b0", 16);
		var dispatcher = Create(queue);
		var subscription = dispatcher.Subscribe(new SubscriptionOptions());

		queue.TryWrite(InputEvent.KeyDown(device, 1));
		queue.TryWrite(InputEvent.KeyDown(device, 2));
		await dispatcher.StopAsync();

		var events = await ReadAll(subscription);

		Assert.Equal<long>([1, 2], events.Select(e => e.Sequence));
		Assert.All(events, e => Assert.Equal(0, e.Backend));
	}

	[Fact]
	public async Task QueueOverflow_ProducesAStreamGapAfterTheDeliveredEvents()
	{
		var device = AddDevice(0, "k");
		var queue = new BackendQueue(0, "b0", 2);
		var dispatcher = Create(queue);
		var subscription = dispatcher.Subscribe(new SubscriptionOptions());

		Assert.True(queue.TryWrite(InputEvent.KeyDown(device, 1)));
		Assert.True(queue.TryWrite(InputEvent.KeyDown(device, 2)));
		Assert.False(queue.TryWrite(InputEvent.KeyDown(device, 3)));
		await dispatcher.StopAsync();

		var events = await ReadAll(subscription);

		Assert.Equal<InputEventKind>(
			[InputEventKind.KeyPressed, InputEventKind.KeyPressed, InputEventKind.StreamGap],
			events.Select(e => e.Kind));
		Assert.Equal(new StreamGapEvent(1, StreamGapReason.BackendQueueOverflow), events[2].AsStreamGap());
	}

	[Fact]
	public async Task EventsFromAnUnknownDevice_AreDropped()
	{
		var device = AddDevice(0, "k");
		var queue = new BackendQueue(0, "b0", 16);
		var dispatcher = Create(queue);
		var subscription = dispatcher.Subscribe(new SubscriptionOptions());

		queue.TryWrite(InputEvent.KeyDown(new DeviceId(999), 1));
		queue.TryWrite(InputEvent.KeyDown(device, 2));
		await dispatcher.StopAsync();

		var events = await ReadAll(subscription);

		Assert.Equal<int>([2], events.Select(e => e.Int0));
	}

	[Fact]
	public async Task EventsFromADeviceOwnedByAnotherBackend_AreDropped()
	{
		var own = AddDevice(0, "own");
		var foreign = AddDevice(1, "foreign");
		var queue0 = new BackendQueue(0, "b0", 16);
		var queue1 = new BackendQueue(1, "b1", 16);
		var dispatcher = Create(queue0, queue1);
		var subscription = dispatcher.Subscribe(new SubscriptionOptions());

		queue0.TryWrite(InputEvent.KeyDown(foreign, 1));
		queue0.TryWrite(InputEvent.KeyDown(own, 2));
		await dispatcher.StopAsync();

		var events = await ReadAll(subscription);

		Assert.Equal<int>([2], events.Select(e => e.Int0));
	}

	[Fact]
	public async Task EventsAfterRemoval_AreDropped_ButDisconnectIsDelivered()
	{
		var device = AddDevice(0, "k");
		var queue = new BackendQueue(0, "b0", 16);
		var dispatcher = Create(queue);
		var subscription = dispatcher.Subscribe(new SubscriptionOptions());

		_registry.Remove(device, 0, out _);
		queue.TryWrite(InputEvent.Disconnected(device));
		queue.TryWrite(InputEvent.KeyDown(device, 1));
		await dispatcher.StopAsync();

		var events = await ReadAll(subscription);

		Assert.Equal<InputEventKind>([InputEventKind.DeviceDisconnected], events.Select(e => e.Kind));
	}

	[Fact]
	public async Task AggregateDevices_MarkTheirEvents()
	{
		var device = AddDevice(0, "system", DeviceOrigin.Aggregate);
		var queue = new BackendQueue(0, "b0", 16);
		var dispatcher = Create(queue);
		var subscription = dispatcher.Subscribe(new SubscriptionOptions());

		queue.TryWrite(InputEvent.KeyDown(device, 1));
		await dispatcher.StopAsync();

		var events = await ReadAll(subscription);

		Assert.True((Assert.Single(events).Flags & InputEventFlags.Aggregate) != 0);
	}

	[Fact]
	public async Task SeveralQueues_ShareOneSequence()
	{
		var first = AddDevice(0, "a");
		var second = AddDevice(1, "b");
		var queue0 = new BackendQueue(0, "b0", 16);
		var queue1 = new BackendQueue(1, "b1", 16);
		var dispatcher = Create(queue0, queue1);
		var subscription = dispatcher.Subscribe(new SubscriptionOptions());

		queue0.TryWrite(InputEvent.KeyDown(first, 1));
		queue1.TryWrite(InputEvent.KeyDown(second, 2));
		queue0.TryWrite(InputEvent.KeyDown(first, 3));
		await dispatcher.StopAsync();

		var events = await ReadAll(subscription);

		Assert.Equal<long>([1, 2, 3], events.Select(e => e.Sequence));
		// Внутри одного backend порядок сохраняется.
		Assert.Equal<int>([1, 3], events.Where(e => e.Backend == 0).Select(e => e.Int0));
	}

	[Fact]
	public async Task SubscribingAfterStop_ReturnsACompletedSubscription()
	{
		var dispatcher = Create(new BackendQueue(0, "b0", 4));
		await dispatcher.StopAsync();

		var subscription = dispatcher.Subscribe(new SubscriptionOptions());

		Assert.Empty(await ReadAll(subscription));
	}

	[Fact]
	public async Task DisposedSubscription_ReceivesNothingMore()
	{
		var device = AddDevice(0, "k");
		var queue = new BackendQueue(0, "b0", 16);
		var dispatcher = Create(queue);
		var disposed = dispatcher.Subscribe(new SubscriptionOptions());
		var active = dispatcher.Subscribe(new SubscriptionOptions());

		await disposed.DisposeAsync();
		queue.TryWrite(InputEvent.KeyDown(device, 1));
		await dispatcher.StopAsync();

		Assert.Empty(await ReadAll(disposed));
		Assert.Single(await ReadAll(active));
	}
}

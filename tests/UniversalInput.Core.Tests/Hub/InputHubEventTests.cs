using UniversalInput.Core.Hub;
using UniversalInput.Testing.Harness;
using UniversalInput.Testing.Mock;

namespace UniversalInput.Core.Tests.Hub;

public class InputHubEventTests
{
	private static DeviceDescriptor Keyboard(string key = "kbd") =>
		MockInputBackend.Describe(key, InputDeviceKind.Keyboard);

	private static async Task<(InputHub Hub, MockInputBackend Backend)> StartAsync(params string[] devices)
	{
		var backend = new MockInputBackend();
		foreach (var key in devices)
		{
			backend.InitialDevices.Add(Keyboard(key));
		}

		var hub = TestInputHub.Create(backend);
		await hub.StartAsync();
		return (hub, backend);
	}

	[Fact]
	public async Task Subscriber_SeesConnectedBeforeInput_WithIncreasingSequence()
	{
		var backend = new MockInputBackend();
		backend.InitialDevices.Add(Keyboard());
		await using var hub = TestInputHub.Create(backend);
		await using var subscription = hub.Subscribe();
		await hub.StartAsync();

		backend.Emit(InputEvent.KeyDown(backend.IdOf("kbd"), KeyCodes.A));
		var events = await subscription.ReadEventsAsync(2);

		Assert.Equal(InputEventKind.DeviceConnected, events[0].Kind);
		Assert.Equal(InputEventKind.KeyPressed, events[1].Kind);
		Assert.True(events[1].Sequence > events[0].Sequence);
		Assert.Equal(KeyCodes.A, events[1].AsKey().Usage);
	}

	[Fact]
	public async Task EventsOfOneDevice_KeepTheirOrder()
	{
		var (hub, backend) = await StartAsync("kbd");
		await using var owner = hub;
		await using var subscription = hub.Subscribe(new SubscriptionOptions { Kinds = [InputEventKind.KeyPressed] });
		var device = backend.IdOf("kbd");

		for (var i = 0; i < 300; i++)
		{
			Assert.True(backend.Emit(InputEvent.KeyDown(device, i)));
		}

		var events = await subscription.ReadEventsAsync(300);

		Assert.Equal<int>(Enumerable.Range(0, 300), events.Select(e => e.Int0));
		for (var i = 1; i < events.Count; i++)
		{
			Assert.True(events[i].Sequence > events[i - 1].Sequence);
		}
	}

	[Fact]
	public async Task Disconnect_RemovesTheDevice_AndEmitsDeviceDisconnected()
	{
		var (hub, backend) = await StartAsync("kbd");
		await using var owner = hub;
		await using var subscription = hub.Subscribe(new SubscriptionOptions { Kinds = [InputEventKind.DeviceDisconnected] });
		var device = backend.IdOf("kbd");

		backend.Disconnect(device);

		var disconnected = Assert.Single(await subscription.ReadEventsAsync(1));
		Assert.Equal(device, disconnected.Device);
		Assert.Empty(hub.Devices);
	}

	[Fact]
	public async Task Connect_AfterStart_AddsTheDeviceAndAnnouncesIt()
	{
		var (hub, backend) = await StartAsync();
		await using var owner = hub;
		await using var subscription = hub.Subscribe();

		var id = backend.Connect(Keyboard("late"));

		var connected = Assert.Single(await subscription.ReadEventsAsync(1));
		Assert.Equal(InputEventKind.DeviceConnected, connected.Kind);
		Assert.Equal(id, connected.Device);
		Assert.Equal("late", Assert.Single(hub.Devices).Name);
	}

	[Fact]
	public async Task EventsFromUnknownOrRemovedDevices_AreNotDelivered()
	{
		var (hub, backend) = await StartAsync("old", "live");
		await using var owner = hub;
		await using var subscription = hub.Subscribe(new SubscriptionOptions { Kinds = [InputEventKind.KeyPressed] });
		var old = backend.IdOf("old");
		var live = backend.IdOf("live");

		backend.Disconnect(old);
		backend.Emit(InputEvent.KeyDown(old, 1));
		backend.Emit(InputEvent.KeyDown(new DeviceId(999), 2));
		backend.Emit(InputEvent.KeyDown(live, 3));

		var events = await subscription.ReadEventsAsync(1);

		Assert.Equal(3, Assert.Single(events).Int0);
	}

	[Fact]
	public async Task AggregateDevice_MarksItsEvents()
	{
		var backend = new MockInputBackend();
		backend.InitialDevices.Add(
			MockInputBackend.Describe("system", InputDeviceKind.SystemWide, InputCapabilities.Keys, DeviceOrigin.Aggregate));
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync();
		await using var subscription = hub.Subscribe(new SubscriptionOptions { Kinds = [InputEventKind.KeyPressed] });

		backend.Emit(InputEvent.KeyDown(backend.IdOf("system"), 1));

		var inputEvent = Assert.Single(await subscription.ReadEventsAsync(1));
		Assert.True((inputEvent.Flags & InputEventFlags.Aggregate) != 0);
	}

	[Fact]
	public async Task Subscriptions_FilterByDeviceAndBackend()
	{
		var first = new MockInputBackend("first");
		first.InitialDevices.Add(Keyboard("a"));
		var second = new MockInputBackend("second");
		second.InitialDevices.Add(Keyboard("b"));
		await using var hub = TestInputHub.Create(first, second);
		await hub.StartAsync();
		await using var onlySecond = hub.Subscribe(new SubscriptionOptions
		{
			BackendIds = ["second"],
			Kinds = [InputEventKind.KeyPressed],
		});

		first.Emit(InputEvent.KeyDown(first.IdOf("a"), 1));
		second.Emit(InputEvent.KeyDown(second.IdOf("b"), 2));

		var inputEvent = Assert.Single(await onlySecond.ReadEventsAsync(1));
		Assert.Equal(2, inputEvent.Int0);
		Assert.Equal(hub.Backends[1].Index, inputEvent.Backend);
	}

	[Fact]
	public async Task Subscribe_WithAnUnknownBackendId_Throws()
	{
		var (hub, _) = await StartAsync("kbd");
		await using var owner = hub;

		Assert.Throws<ArgumentException>(() => hub.Subscribe(new SubscriptionOptions { BackendIds = ["nope"] }));
	}

	[Fact]
	public async Task SlowSubscriber_DoesNotBlockOthers_AndSeesAGap()
	{
		var (hub, backend) = await StartAsync("kbd");
		await using var owner = hub;
		var device = backend.IdOf("kbd");
		await using var slow = hub.Subscribe(new SubscriptionOptions { Capacity = 4, Kinds = [InputEventKind.KeyPressed] });
		await using var fast = hub.Subscribe(new SubscriptionOptions { Kinds = [InputEventKind.KeyPressed] });

		for (var i = 0; i < 50; i++)
		{
			backend.Emit(InputEvent.KeyDown(device, i));
		}

		var received = await fast.ReadEventsAsync(50);
		Assert.Equal(50, received.Count);

		List<InputEvent> queued = [];
		while (slow.Reader.TryRead(out var inputEvent))
		{
			queued.Add(inputEvent);
		}

		Assert.Contains(queued, e => e.Kind == InputEventKind.StreamGap);
		Assert.True(slow.DroppedCount > 0);
		Assert.Equal(49, queued[^1].Int0);
	}

	[Fact]
	public async Task SlowSubscriber_ContinuousMotionIsCoalescedWithoutLoss()
	{
		var backend = new MockInputBackend();
		backend.InitialDevices.Add(MockInputBackend.Describe("mouse", InputDeviceKind.Mouse, InputCapabilities.PointerMotion));
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync();
		var device = backend.IdOf("mouse");
		await using var slow = hub.Subscribe(new SubscriptionOptions { Capacity = 4, Kinds = [InputEventKind.MouseMoved] });
		await using var marker = hub.Subscribe(new SubscriptionOptions { Kinds = [InputEventKind.KeyPressed] });

		for (var i = 0; i < 100; i++)
		{
			backend.Emit(InputEvent.MouseMove(device, 1, 0));
		}

		backend.Emit(InputEvent.KeyDown(device, KeyCodes.Enter));
		await marker.ReadEventsAsync(1);

		List<InputEvent> queued = [];
		while (slow.Reader.TryRead(out var inputEvent))
		{
			queued.Add(inputEvent);
		}

		Assert.Equal(100f, queued.Sum(e => e.V0));
		Assert.Equal(4, queued.Count);
		Assert.Equal(0, slow.DroppedCount);
		Assert.True((queued[^1].Flags & InputEventFlags.Coalesced) != 0);
	}

	[Fact]
	public async Task ReadEventsAsync_StreamsEventsUntilCancelled()
	{
		var (hub, backend) = await StartAsync("kbd");
		await using var owner = hub;
		var device = backend.IdOf("kbd");
		using var cts = new CancellationTokenSource();
		List<InputEvent> received = [];

		var reader = Task.Run(async () =>
		{
			try
			{
				await foreach (var inputEvent in hub.ReadEventsAsync(
					new SubscriptionOptions { Kinds = [InputEventKind.KeyPressed] }, cts.Token))
				{
					received.Add(inputEvent);
					await cts.CancelAsync();
				}
			}
			catch (OperationCanceledException)
			{
			}
		});

		// Подписка создаётся при начале перечисления, поэтому событие отправляем, пока читатель не получит его.
		while (!reader.IsCompleted)
		{
			backend.Emit(InputEvent.KeyDown(device, KeyCodes.Q));
			await Task.Delay(5);
		}

		await reader.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.NotEmpty(received);
		Assert.Equal(KeyCodes.Q, received[0].AsKey().Usage);
	}
}

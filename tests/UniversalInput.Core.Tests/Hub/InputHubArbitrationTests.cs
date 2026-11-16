using UniversalInput.Core.Hub;
using UniversalInput.Testing.Harness;
using UniversalInput.Testing.Mock;

namespace UniversalInput.Core.Tests.Hub;

public class InputHubArbitrationTests
{
	private static DeviceDescriptor Keyboard(string key) =>
		MockInputBackend.Describe(key, InputDeviceKind.Keyboard) with
		{
			Bus = DeviceBus.Usb,
			Identity = new DeviceIdentity(VendorId: 0x046D, ProductId: 0xC31C, SerialNumber: "KB-1"),
		};

	/// <summary>Одна клавиатура, которую видят и Native (владелец Observe/Emulate), и Hid (владелец DirectAccess).</summary>
	private static async Task<(InputHub Hub, MockInputBackend Native, MockInputBackend Hid, DeviceId Id)> StartSharedAsync()
	{
		var native = new MockInputBackend("native");
		var hid = new MockInputBackend("hid");
		var hub = TestInputHub.Create(native, hid);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		var id = native.Connect(Keyboard("native:kbd"));
		Assert.Equal(id, hid.Connect(Keyboard("hid:kbd")));
		return (hub, native, hid, id);
	}

	[Fact]
	public async Task OneLogicalDevice_IsAnnouncedOnce_AndRemovedWithItsLastProvider()
	{
		var native = new MockInputBackend("native");
		var hid = new MockInputBackend("hid");
		await using var hub = TestInputHub.Create(native, hid);
		await using var subscription = hub.Subscribe(new SubscriptionOptions
		{
			Kinds = [InputEventKind.DeviceConnected, InputEventKind.DeviceDisconnected],
		});
		await hub.StartAsync(TestContext.Current.CancellationToken);

		var id = native.Connect(Keyboard("native:kbd"));
		hid.Connect(Keyboard("hid:kbd"));
		native.Disconnect(id);
		hid.Disconnect(id);
		var events = await subscription.ReadEventsAsync(2);

		Assert.Equal<InputEventKind>([InputEventKind.DeviceConnected, InputEventKind.DeviceDisconnected], events.Select(e => e.Kind));
		Assert.Empty(hub.Devices);
	}

	[Fact]
	public async Task OnlyTheObserveOwner_DeliversEvents()
	{
		var (hub, native, hid, id) = await StartSharedAsync();
		await using var owner = hub;
		await using var subscription = hub.Subscribe(new SubscriptionOptions { Kinds = [InputEventKind.KeyPressed] });

		native.Emit(InputEvent.KeyDown(id, 1));
		hid.Emit(InputEvent.KeyDown(id, 2));
		native.Emit(InputEvent.KeyDown(id, 3));
		var events = await subscription.ReadEventsAsync(2);

		Assert.Equal<int>([1, 3], events.Select(e => e.Int0));
		Assert.Equal<ushort>([0, 0], events.Select(e => e.Backend));
	}

	[Fact]
	public async Task WhenTheOwnerLeaves_TheOtherBackendTakesOver_WithoutADisconnect()
	{
		var (hub, native, hid, id) = await StartSharedAsync();
		await using var owner = hub;
		await using var subscription = hub.Subscribe(new SubscriptionOptions
		{
			Kinds = [InputEventKind.KeyPressed, InputEventKind.DeviceDisconnected],
		});

		native.Disconnect(id);
		hid.Emit(InputEvent.KeyDown(id, 5));

		// Диспетчер работает асинхронно: событие нужно дождаться до отключения устройства,
		// иначе оно будет отброшено как событие уже удалённого устройства.
		var first = Assert.Single(await subscription.ReadEventsAsync(1));
		hid.Disconnect(id);
		var second = Assert.Single(await subscription.ReadEventsAsync(1));

		Assert.Equal(InputEventKind.KeyPressed, first.Kind);
		Assert.Equal(5, first.Int0);
		Assert.Equal(InputEventKind.DeviceDisconnected, second.Kind);
	}

	[Fact]
	public async Task Emulation_GoesToTheEmulateOwner()
	{
		var (hub, native, hid, id) = await StartSharedAsync();
		await using var owner = hub;

		await hub.EmulateAsync(InputEvent.KeyDown(id, 7), TestContext.Current.CancellationToken);

		Assert.Single(native.Emulated);
		Assert.Empty(hid.Emulated);
	}

	[Fact]
	public async Task DirectAccess_GoesToTheRawAccessOwner()
	{
		var (hub, native, hid, id) = await StartSharedAsync();
		await using var owner = hub;

		await using var channel = await hub.OpenRawAsync(id, RawAccessMode.ReadWrite, TestContext.Current.CancellationToken);

		Assert.NotNull(hid.GetChannel(id));
		Assert.Null(native.GetChannel(id));
	}

	[Fact]
	public async Task RawChannel_ClosesWhenItsProviderLeaves_ButTheDeviceStays()
	{
		var (hub, native, hid, id) = await StartSharedAsync();
		await using var owner = hub;
		var channel = await hub.OpenRawAsync(id, RawAccessMode.Read, TestContext.Current.CancellationToken);
		var pending = channel.ReadAsync(new byte[8], TestContext.Current.CancellationToken).AsTask();

		hid.Disconnect(id);

		var error = await Assert.ThrowsAsync<InputException>(() => pending);
		Assert.Equal(InputErrorCode.DeviceUnavailable, error.Code);
		Assert.Single(hub.Devices);
	}

	[Fact]
	public async Task RoleOwners_AreVisibleOnTheDevice()
	{
		var (hub, native, hid, id) = await StartSharedAsync();
		await using var owner = hub;

		var device = Assert.Single(hub.Devices);

		Assert.Equal<string>(["native", "hid"], device.Providers);
		Assert.Equal("native", device.RoleOwners[InputOperation.Observe]);
		Assert.Equal("hid", device.RoleOwners[InputOperation.DirectAccess]);
		Assert.True(hub.GetSupport(InputOperation.Emulate, id).IsSupported);
	}

	[Fact]
	public async Task AggregateEvents_AreDuplicates_WhileDevicesObserveKeyboardAndMouse()
	{
		var native = new MockInputBackend("native");
		var hook = new MockInputBackend("hook");
		await using var hub = TestInputHub.Create(native, hook);
		await using var plain = hub.Subscribe(new SubscriptionOptions { Kinds = [InputEventKind.KeyPressed] });
		await using var explicitAggregate = hub.Subscribe(new SubscriptionOptions
		{
			Kinds = [InputEventKind.KeyPressed],
			Origins = [DeviceOrigin.Aggregate, DeviceOrigin.Physical],
		});
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var keyboard = native.Connect(MockInputBackend.Describe("kbd", InputDeviceKind.Keyboard));
		var system = hook.Connect(MockInputBackend.Describe("system", InputDeviceKind.SystemWide, origin: DeviceOrigin.Aggregate));

		hook.Emit(InputEvent.KeyDown(system, 1));
		native.Emit(InputEvent.KeyDown(keyboard, 2));
		native.Emit(InputEvent.KeyDown(keyboard, 99));

		var seenByPlain = await plain.ReadEventsAsync(2);
		var seenByExplicit = await explicitAggregate.ReadEventsAsync(3);
		Assert.Equal<int>([2, 99], seenByPlain.Select(e => e.Int0));
		Assert.Equal<int>([1, 2, 99], seenByExplicit.Select(e => e.Int0).Order());
	}

	[Fact]
	public async Task AggregateEvents_Pass_WhenNoDeviceObservesTheSameCategory()
	{
		var hook = new MockInputBackend("hook");
		await using var hub = TestInputHub.Create(hook);
		await using var subscription = hub.Subscribe(new SubscriptionOptions { Kinds = [InputEventKind.KeyPressed] });
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var system = hook.Connect(MockInputBackend.Describe("system", InputDeviceKind.SystemWide, origin: DeviceOrigin.Aggregate));

		hook.Emit(InputEvent.KeyDown(system, 1));
		var events = await subscription.ReadEventsAsync(1);

		var received = Assert.Single(events);
		Assert.True((received.Flags & InputEventFlags.Aggregate) != 0);
	}

	[Fact]
	public async Task AggregateKeyboardEvents_StayWhenOnlyAMouseIsObserved()
	{
		var native = new MockInputBackend("native");
		var hook = new MockInputBackend("hook");
		await using var hub = TestInputHub.Create(native, hook);
		await using var subscription = hub.Subscribe(new SubscriptionOptions { Kinds = [InputEventKind.KeyPressed] });
		await hub.StartAsync(TestContext.Current.CancellationToken);
		native.Connect(MockInputBackend.Describe("mouse", InputDeviceKind.Mouse));
		var system = hook.Connect(MockInputBackend.Describe("system", InputDeviceKind.SystemWide, origin: DeviceOrigin.Aggregate));

		hook.Emit(InputEvent.KeyDown(system, 1));
		var events = await subscription.ReadEventsAsync(1);

		Assert.Equal(1, Assert.Single(events).Int0);
	}
}

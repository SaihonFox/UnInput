using UniversalInput.Backends.Hid.Discovery;
using UniversalInput.Backends.Hid.Options;
using UniversalInput.Backends.Hid.Tests.Fakes;
using UniversalInput.Core.Hub;
using UniversalInput.Testing.Harness;

namespace UniversalInput.Backends.Hid.Tests.Observation;

public class HidObservationTests
{
	private static readonly SubscriptionOptions InputKinds = new()
	{
		Kinds =
		[
			InputEventKind.KeyPressed,
			InputEventKind.KeyReleased,
			InputEventKind.MouseMoved,
			InputEventKind.MouseButtonPressed,
			InputEventKind.MouseButtonReleased,
			InputEventKind.MouseWheel,
		],
	};

	private static (InputHub Hub, FakeHidPlatform Platform) Create(bool observe, params HidDeviceInfo[] devices)
	{
		var platform = new FakeHidPlatform();
		platform.SetDevices(devices);
		var options = new HidBackendOptions { RescanInterval = TimeSpan.FromMinutes(5), ObserveInputDevices = observe };
		return (TestInputHub.Create(new HidBackend(platform, options)), platform);
	}

	private static DeviceId IdOf(InputHub hub, string path) => hub.Devices.Single(d => d.Identity?.Path == path).Id;

	[Fact]
	public async Task ByDefault_ObserveIsUnsupported_AndTheDeviceIsNotOpened()
	{
		var (hub, platform) = Create(false, FakeHidPlatform.KeyboardWithDescriptor("kb0"));
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		var support = hub.Devices.Single().Operations[InputOperation.Observe];
		Assert.Equal(SupportState.Unsupported, support.State);
		Assert.Contains("ObserveInputDevices", support.Reason);
		Assert.Empty(platform.Connections);
	}

	[Fact]
	public async Task WhenEnabled_KeyboardsAndMice_AreObservable_OthersAreNot()
	{
		var (hub, _) = Create(
			true,
			FakeHidPlatform.KeyboardWithDescriptor("kb0"),
			FakeHidPlatform.MouseWithDescriptor("m0"),
			FakeHidPlatform.GamepadWithDescriptor("pad0"),
			FakeHidPlatform.Keyboard("nodescriptor"));
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		Assert.True(hub.GetSupport(InputOperation.Observe, IdOf(hub, "kb0")).IsSupported);
		Assert.True(hub.GetSupport(InputOperation.Observe, IdOf(hub, "m0")).IsSupported);
		Assert.False(hub.GetSupport(InputOperation.Observe, IdOf(hub, "pad0")).IsSupported);
		Assert.False(hub.GetSupport(InputOperation.Observe, IdOf(hub, "nodescriptor")).IsSupported);
		Assert.Contains("нет клавиатуры или мыши", hub.Devices.Single(d => d.Identity?.Path == "pad0").Operations[InputOperation.Observe].Reason);
		Assert.Contains("недоступен", hub.Devices.Single(d => d.Identity?.Path == "nodescriptor").Operations[InputOperation.Observe].Reason);
	}

	[Fact]
	public async Task KeyboardReports_BecomeKeyEvents_OnTheRightDevice()
	{
		var (hub, platform) = Create(true, FakeHidPlatform.KeyboardWithDescriptor("kb0"));
		await using var owner = hub;
		await using var subscription = hub.Subscribe(InputKinds);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		platform.Connections["kb0"].Push(0, 0, 0, 0x04, 0, 0, 0, 0, 0);
		platform.Connections["kb0"].Push(0, 0, 0, 0, 0, 0, 0, 0, 0);
		var events = await subscription.ReadEventsAsync(2);

		Assert.Equal<InputEventKind>([InputEventKind.KeyPressed, InputEventKind.KeyReleased], events.Select(e => e.Kind));
		Assert.All(events, e => Assert.Equal(0x04, e.AsKey().Usage));
		Assert.All(events, e => Assert.Equal(IdOf(hub, "kb0"), e.Device));
	}

	[Fact]
	public async Task MouseReports_BecomeMotionButtonsAndWheel()
	{
		var (hub, platform) = Create(true, FakeHidPlatform.MouseWithDescriptor("m0"));
		await using var owner = hub;
		await using var subscription = hub.Subscribe(InputKinds);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		platform.Connections["m0"].Push(0, 0b001, 5, 0xFE, 1);
		var events = await subscription.ReadEventsAsync(3);

		Assert.Equal<InputEventKind>(
			[InputEventKind.MouseMoved, InputEventKind.MouseButtonPressed, InputEventKind.MouseWheel], events.Select(e => e.Kind));
		Assert.Equal(5f, events[0].AsPointer().X);
		Assert.Equal(-2f, events[0].AsPointer().Y);
		Assert.Equal(1, events[1].AsMouseButton().Button);
		Assert.Equal(1f, events[2].AsWheel().DeltaY);
	}

	[Fact]
	public async Task WhenTheDeviceCannotBeOpened_ObserveIsUnsupported_WithTheReasonAndAHint()
	{
		var (hub, platform) = Create(true, FakeHidPlatform.KeyboardWithDescriptor("kb0"));
		platform.OpenFailure = new IOException("Unable to open HID class device");
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		var support = hub.Devices.Single().Operations[InputOperation.Observe];
		Assert.Equal(SupportState.Unsupported, support.State);
		Assert.Contains("Unable to open", support.Reason);
		Assert.False(string.IsNullOrEmpty(support.Remediation));
		Assert.Equal(SupportState.Supported, hub.Devices.Single().Operations[InputOperation.DirectAccess].State);
	}

	[Fact]
	public async Task AnObservedDevice_CannotBeOpenedForRawAccess()
	{
		var (hub, _) = Create(true, FakeHidPlatform.KeyboardWithDescriptor("kb0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		var error = await Assert.ThrowsAsync<InputException>(
			() => hub.OpenRawAsync(IdOf(hub, "kb0"), RawAccessMode.Read, TestContext.Current.CancellationToken).AsTask());

		Assert.Equal(InputErrorCode.DeviceBusy, error.Code);
		Assert.Contains("ObserveInputDevices", error.Message);
	}

	[Fact]
	public async Task WhenTheDeviceDisappears_ItsConnectionIsClosed()
	{
		var (hub, platform) = Create(true, FakeHidPlatform.KeyboardWithDescriptor("kb0"), FakeHidPlatform.MouseWithDescriptor("m0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var connection = platform.Connections["kb0"];

		platform.SetDevices(FakeHidPlatform.MouseWithDescriptor("m0"));
		platform.RaiseChanged();

		await Wait.UntilAsync(() => connection.IsDisposed, description: "соединение отключённой клавиатуры закрывается");
		Assert.False(platform.Connections["m0"].IsDisposed);
	}

	[Fact]
	public async Task Stop_ClosesEveryObservedConnection()
	{
		var (hub, platform) = Create(true, FakeHidPlatform.KeyboardWithDescriptor("kb0"), FakeHidPlatform.MouseWithDescriptor("m0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		await hub.StopAsync(TestContext.Current.CancellationToken);

		Assert.All(platform.Connections.Values, connection => Assert.True(connection.IsDisposed));
	}

	[Fact]
	public async Task ALostDevice_EndsObservation_WithoutFaultingTheBackend()
	{
		var (hub, platform) = Create(true, FakeHidPlatform.KeyboardWithDescriptor("kb0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		platform.Connections["kb0"].Fail(new IOException("device lost"));
		await Task.Delay(100, TestContext.Current.CancellationToken);

		Assert.Equal(BackendState.Running, Assert.Single(hub.Backends).State);
	}

	[Fact]
	public async Task Observing_MakesTheObserveRoleAvailable_ForPerDeviceArbitration()
	{
		var (hub, _) = Create(true, FakeHidPlatform.KeyboardWithDescriptor("kb0"));
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		Assert.Contains(InputOperation.Observe, Assert.Single(hub.Backends).Roles);
		Assert.True(hub.GetSupport(InputOperation.Observe, IdOf(hub, "kb0")).IsSupported);
	}
}

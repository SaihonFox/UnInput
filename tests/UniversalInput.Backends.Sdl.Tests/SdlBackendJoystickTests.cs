using UniversalInput.Backends.Sdl.Options;
using UniversalInput.Backends.Sdl.Runtime;
using UniversalInput.Backends.Sdl.Tests.Fakes;
using UniversalInput.Core.Hub;
using UniversalInput.Testing.Harness;

namespace UniversalInput.Backends.Sdl.Tests;

public class SdlBackendJoystickTests
{
	private static readonly SubscriptionOptions StickEvents = new()
	{
		Kinds = [InputEventKind.GamepadButtonPressed, InputEventKind.GamepadButtonReleased, InputEventKind.GamepadAxisChanged],
	};

	private static (InputHub Hub, List<FakeSdlRuntime> Runtimes) Create(bool include, Action<FakeSdlRuntime>? setup = null)
	{
		List<FakeSdlRuntime> runtimes = [];
		var options = new SdlBackendOptions
		{
			PollInterval = TimeSpan.FromMilliseconds(20),
			IncludeUnmappedJoysticks = include,
		};

		var backend = new SdlBackend(
			() =>
			{
				var runtime = new FakeSdlRuntime();
				setup?.Invoke(runtime);
				lock (runtimes)
				{
					runtimes.Add(runtime);
				}

				return runtime;
			},
			() => BackendProbe.Available,
			options);

		return (TestInputHub.Create(backend), runtimes);
	}

	private static void OneStick(FakeSdlRuntime runtime) => runtime.Plug(FakeSdlRuntime.Stick(5, "Flight stick", axes: 3, buttons: 12, hats: 1));

	[Fact]
	public async Task ByDefault_UnmappedJoysticksAreNotShown()
	{
		var (hub, _) = Create(false, OneStick);
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		Assert.Empty(hub.Devices);
	}

	[Fact]
	public async Task WhenEnabled_AJoystickIsShown_WithItsShapeAndHonestOperations()
	{
		var (hub, _) = Create(true, OneStick);
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		var stick = Assert.Single(hub.Devices);
		Assert.Equal("Flight stick", stick.Name);
		Assert.Equal(InputDeviceKind.Joystick, stick.Kind);
		Assert.Equal(InputCapabilities.GamepadButtons | InputCapabilities.Axes, stick.Capabilities);
		Assert.Equal("none", stick.Properties["mapping"]);
		Assert.Equal("3", stick.Properties["axes"]);
		Assert.Equal("12", stick.Properties["buttons"]);
		Assert.Equal("1", stick.Properties["hats"]);
		Assert.Equal((ushort)0x044F, stick.Identity?.VendorId);
		Assert.True(hub.GetSupport(InputOperation.Observe, stick.Id).IsSupported);
		Assert.False(hub.GetSupport(InputOperation.Output, stick.Id).IsSupported);
		Assert.False(hub.GetSupport(InputOperation.Emulate, stick.Id).IsSupported);
	}

	[Fact]
	public async Task GamepadsAndJoysticks_AreShownSideBySide_EachOnItsOwnPath()
	{
		var (hub, _) = Create(true, runtime =>
		{
			runtime.Plug(FakeSdlRuntime.Pad(1));
			runtime.Plug(FakeSdlRuntime.Stick(5));
		});
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		Assert.Equal(2, hub.Devices.Count);
		Assert.Single(hub.Devices, d => d.Kind == InputDeviceKind.Gamepad);
		Assert.Single(hub.Devices, d => d.Kind == InputDeviceKind.Joystick);
	}

	[Fact]
	public async Task ButtonsAxesAndHats_ArriveWithTheDevicesOwnNumbers()
	{
		var (hub, runtimes) = Create(true, OneStick);
		await using var owner = hub;
		await using var subscription = hub.Subscribe(StickEvents);
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var id = hub.Devices.Single().Id;

		runtimes[0].Push(SdlEvent.JoystickButtonDown(5, 9));
		runtimes[0].Push(SdlEvent.JoystickButtonUp(5, 9));
		runtimes[0].Push(SdlEvent.JoystickAxis(5, 2, short.MaxValue));
		runtimes[0].Push(SdlEvent.JoystickHat(5, 0, 0b0011));
		var events = await subscription.ReadEventsAsync(5);

		Assert.All(events, e => Assert.Equal(id, e.Device));
		Assert.Equal<InputEventKind>(
			[
				InputEventKind.GamepadButtonPressed,
				InputEventKind.GamepadButtonReleased,
				InputEventKind.GamepadAxisChanged,
				InputEventKind.GamepadAxisChanged,
				InputEventKind.GamepadAxisChanged,
			],
			events.Select(e => e.Kind));
		Assert.Equal(9, events[0].Int0);
		Assert.Equal(9, events[1].Int0);
		Assert.Equal((2, 1f), (events[2].AsAxis().Axis, events[2].AsAxis().Value));

		// Hat 0 у устройства с тремя осями — оси 3 (X) и 4 (Y): вправо и вверх (вверх отрицательно).
		Assert.Equal((3, 1f), (events[3].AsAxis().Axis, events[3].AsAxis().Value));
		Assert.Equal((4, -1f), (events[4].AsAxis().Axis, events[4].AsAxis().Value));
	}

	[Fact]
	public async Task AHatThatTheDeviceDoesNotHave_IsIgnored()
	{
		var (hub, runtimes) = Create(true, OneStick);
		await using var owner = hub;
		await using var subscription = hub.Subscribe(StickEvents);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		runtimes[0].Push(SdlEvent.JoystickHat(5, 3, 0b0001));
		runtimes[0].Push(SdlEvent.JoystickButtonDown(5, 1));
		var events = await subscription.ReadEventsAsync(1);

		Assert.Equal(InputEventKind.GamepadButtonPressed, Assert.Single(events).Kind);
	}

	[Fact]
	public async Task EventsOfUnknownJoysticks_AreIgnored()
	{
		var (hub, runtimes) = Create(true, OneStick);
		await using var owner = hub;
		await using var subscription = hub.Subscribe(StickEvents);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		runtimes[0].Push(SdlEvent.JoystickButtonDown(99, 1));
		runtimes[0].Push(SdlEvent.JoystickButtonDown(5, 2));
		var events = await subscription.ReadEventsAsync(1);

		Assert.Equal(2, Assert.Single(events).Int0);
	}

	[Fact]
	public async Task HotPlug_AddsAndRemovesAJoystick_AndClosesIt()
	{
		var (hub, runtimes) = Create(true);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		Assert.Empty(hub.Devices);

		runtimes[0].Plug(FakeSdlRuntime.Stick(8, "Pedals", axes: 2, buttons: 0, hats: 0));
		runtimes[0].Push(SdlEvent.JoystickAdded(8));
		await Wait.UntilAsync(() => hub.Devices.Count == 1, description: "педали появляются");

		Assert.Equal(InputCapabilities.Axes, hub.Devices.Single().Capabilities);

		runtimes[0].Push(SdlEvent.JoystickRemoved(8));
		await Wait.UntilAsync(() => hub.Devices.Count == 0, description: "педали исчезают");
		Assert.Equal<uint>([8], runtimes[0].ClosedJoysticks);
	}

	[Fact]
	public async Task AJoystickThatCannotBeOpened_IsSkipped()
	{
		var (hub, runtimes) = Create(true, runtime =>
		{
			OneStick(runtime);
			runtime.JoystickOpenFailures.Add(5);
		});
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		Assert.Empty(hub.Devices);
		Assert.Single(runtimes);
	}

	[Fact]
	public async Task WithTheOptionOff_JoystickAddedEventsAreIgnored()
	{
		var (hub, runtimes) = Create(false);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		runtimes[0].Plug(FakeSdlRuntime.Stick(8));
		runtimes[0].Push(SdlEvent.JoystickAdded(8));
		await Task.Delay(150, TestContext.Current.CancellationToken);

		Assert.Empty(hub.Devices);
	}
}

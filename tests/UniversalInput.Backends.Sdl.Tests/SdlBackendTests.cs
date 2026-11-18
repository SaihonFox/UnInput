using UniversalInput.Backends.Sdl.Options;
using UniversalInput.Backends.Sdl.Runtime;
using UniversalInput.Backends.Sdl.Tests.Fakes;
using UniversalInput.Core.Hub;
using UniversalInput.Testing.Harness;

namespace UniversalInput.Backends.Sdl.Tests;

public class SdlBackendTests
{
	private static readonly SubscriptionOptions PadEvents = new()
	{
		Kinds =
		[
			InputEventKind.GamepadButtonPressed,
			InputEventKind.GamepadButtonReleased,
			InputEventKind.GamepadAxisChanged,
			InputEventKind.SensorChanged,
		],
	};

	/// <summary>Среда создаётся заново при каждом запуске, как и настоящая: подготовка применяется к каждой.</summary>
	private static (InputHub Hub, List<FakeSdlRuntime> Runtimes) Create(
		Action<FakeSdlRuntime>? setup = null, Action<SdlBackendOptions>? configure = null)
	{
		List<FakeSdlRuntime> runtimes = [];
		var options = new SdlBackendOptions { PollInterval = TimeSpan.FromMilliseconds(20) };
		configure?.Invoke(options);

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

	private static void OnePad(FakeSdlRuntime runtime) => runtime.Plug(FakeSdlRuntime.Pad(1));

	[Fact]
	public async Task Start_AddsConnectedGamepads_WithIdentityAndHonestOperations()
	{
		var (hub, _) = Create(runtime => runtime.Plug(FakeSdlRuntime.Pad(1, "Xbox", vendor: 0x045E, product: 0x028E, serial: "SN9", path: "/dev/input/event5")));
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		var pad = Assert.Single(hub.Devices);
		Assert.Equal("Xbox", pad.Name);
		Assert.Equal(InputDeviceKind.Gamepad, pad.Kind);
		Assert.Equal(DeviceOrigin.Physical, pad.Origin);
		Assert.Equal((ushort)0x045E, pad.Identity?.VendorId);
		Assert.Equal((ushort)0x028E, pad.Identity?.ProductId);
		Assert.Equal("SN9", pad.Identity?.SerialNumber);
		Assert.Equal("/dev/input/event5", pad.Identity?.Path);
		Assert.Equal("SDL3", pad.Properties["library"]);
		Assert.True(hub.GetSupport(InputOperation.Observe, pad.Id).IsSupported);
		Assert.True(hub.GetSupport(InputOperation.Output, pad.Id).IsSupported);
		Assert.False(hub.GetSupport(InputOperation.Emulate, pad.Id).IsSupported);
		Assert.False(hub.GetSupport(InputOperation.DirectAccess, pad.Id).IsSupported);
		Assert.Contains(InputOperation.Output, hub.Backends[0].Roles);
	}

	[Fact]
	public async Task Capabilities_FollowWhatTheGamepadReports()
	{
		var (hub, _) = Create(runtime =>
		{
			runtime.Plug(FakeSdlRuntime.Pad(1, "Plain", rumble: false));
			runtime.Plug(FakeSdlRuntime.Pad(2, "Full", path: "/dev/input/event2", rumble: true, accelerometer: true, gyroscope: true));
		});
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		var plain = hub.Devices.Single(d => d.Name == "Plain");
		var full = hub.Devices.Single(d => d.Name == "Full");
		Assert.Equal(InputCapabilities.GamepadButtons | InputCapabilities.Axes, plain.Capabilities);
		Assert.Equal(
			InputCapabilities.GamepadButtons | InputCapabilities.Axes | InputCapabilities.Haptics
				| InputCapabilities.Accelerometer | InputCapabilities.Gyroscope,
			full.Capabilities);
	}

	[Fact]
	public async Task Options_ReachTheRuntime()
	{
		var (hub, runtimes) = Create(OnePad, options =>
		{
			options.EnableSensors = true;
			options.AllowBackgroundEvents = false;
		});
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		Assert.True(runtimes[0].EnableSensorsRequested);
		Assert.False(runtimes[0].AllowBackgroundEvents);
	}

	[Fact]
	public async Task ButtonsAndAxes_ArriveTranslatedAndNormalized()
	{
		var (hub, runtimes) = Create(OnePad);
		await using var owner = hub;
		await using var subscription = hub.Subscribe(PadEvents);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		runtimes[0].Push(SdlEvent.ButtonDown(1, 0));
		runtimes[0].Push(SdlEvent.ButtonUp(1, 0));
		runtimes[0].Push(SdlEvent.AxisMotion(1, 0, short.MaxValue));
		runtimes[0].Push(SdlEvent.AxisMotion(1, 4, 16384));
		var events = await subscription.ReadEventsAsync(4);

		Assert.Equal<InputEventKind>(
			[InputEventKind.GamepadButtonPressed, InputEventKind.GamepadButtonReleased, InputEventKind.GamepadAxisChanged, InputEventKind.GamepadAxisChanged],
			events.Select(e => e.Kind));
		Assert.Equal(GamepadButton.South, events[0].AsGamepadButton().Button);
		Assert.Equal(GamepadButton.South, events[1].AsGamepadButton().Button);
		Assert.Equal((int)GamepadAxis.LeftX, events[2].AsAxis().Axis);
		Assert.Equal(1f, events[2].AsAxis().Value, 0.0001f);
		Assert.Equal((int)GamepadAxis.LeftTrigger, events[3].AsAxis().Axis);
		Assert.Equal(0.5f, events[3].AsAxis().Value, 0.001f);
		Assert.All(events, e => Assert.Equal(hub.Devices[0].Id, e.Device));
	}

	[Fact]
	public async Task ExtraButtons_AndEventsOfUnknownDevices_AreIgnored()
	{
		var (hub, runtimes) = Create(OnePad);
		await using var owner = hub;
		await using var subscription = hub.Subscribe(PadEvents);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		runtimes[0].Push(SdlEvent.ButtonDown(1, 20));
		runtimes[0].Push(SdlEvent.ButtonDown(99, 0));
		runtimes[0].Push(SdlEvent.AxisMotion(1, 9, 100));
		runtimes[0].Push(SdlEvent.ButtonDown(1, 1));
		var events = await subscription.ReadEventsAsync(1);

		Assert.Equal(GamepadButton.East, Assert.Single(events).AsGamepadButton().Button);
	}

	[Fact]
	public async Task Sensors_ArriveForAccelerometerAndGyroscopeOnly()
	{
		var (hub, runtimes) = Create(runtime => runtime.Plug(FakeSdlRuntime.Pad(1, accelerometer: true, gyroscope: true)));
		await using var owner = hub;
		await using var subscription = hub.Subscribe(PadEvents);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		runtimes[0].Push(SdlEvent.SensorUpdate(1, 3, 1, 1, 1));
		runtimes[0].Push(SdlEvent.SensorUpdate(1, 1, 0, 9.8f, 0));
		runtimes[0].Push(SdlEvent.SensorUpdate(1, 2, 0.1f, 0.2f, 0.3f));
		var events = await subscription.ReadEventsAsync(2);

		Assert.Equal(new SensorEvent(SensorKind.Accelerometer, 0, 9.8f, 0), events[0].AsSensor());
		Assert.Equal(new SensorEvent(SensorKind.Gyroscope, 0.1f, 0.2f, 0.3f), events[1].AsSensor());
	}

	[Fact]
	public async Task HotPlug_AddsAndRemovesDevices_AndClosesTheGamepad()
	{
		var (hub, runtimes) = Create();
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		Assert.Empty(hub.Devices);

		runtimes[0].Plug(FakeSdlRuntime.Pad(5, "Late"));
		runtimes[0].Push(SdlEvent.Added(5));
		await Wait.UntilAsync(() => hub.Devices.Count == 1, description: "геймпад должен появиться");

		runtimes[0].Push(SdlEvent.Removed(5));
		await Wait.UntilAsync(() => hub.Devices.Count == 0, description: "геймпад должен исчезнуть");
		Assert.Equal<uint>([5], runtimes[0].Closed);
	}

	[Fact]
	public async Task AnAddedEventForAKnownGamepad_DoesNotDuplicateIt()
	{
		var (hub, runtimes) = Create(OnePad);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		runtimes[0].Push(SdlEvent.Added(1));
		runtimes[0].Plug(FakeSdlRuntime.Pad(2, "Second"));
		runtimes[0].Push(SdlEvent.Added(2));
		await Wait.UntilAsync(() => hub.Devices.Count == 2);

		Assert.Equal(2, hub.Devices.Select(d => d.Key).Distinct().Count());
	}

	[Fact]
	public async Task AGamepadThatCannotBeOpened_IsSkipped()
	{
		var (hub, runtimes) = Create(runtime => runtime.OpenFailures.Add(7));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		runtimes[0].Plug(FakeSdlRuntime.Pad(7));
		runtimes[0].Push(SdlEvent.Added(7));
		runtimes[0].Plug(FakeSdlRuntime.Pad(8, "Good"));
		runtimes[0].Push(SdlEvent.Added(8));

		await Wait.UntilAsync(() => hub.Devices.Count == 1);
		Assert.Equal("Good", hub.Devices[0].Name);
	}

	[Fact]
	public async Task IdenticalGamepadsWithoutPathOrSerial_AreSeparateDevices()
	{
		var (hub, _) = Create(runtime =>
		{
			runtime.Plug(new SdlGamepadInfo(1, "Pad", 0x1234, 0x5678, null, null, true, false, false));
			runtime.Plug(new SdlGamepadInfo(2, "Pad", 0x1234, 0x5678, null, null, true, false, false));
		});
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		Assert.Equal(2, hub.Devices.Count);
		Assert.Equal(2, hub.Devices.Select(d => d.Key).Distinct().Count());
	}

	[Fact]
	public void TheKey_PrefersThePath_ThenTheSerialNumber()
	{
		Assert.Equal("sdl:/dev/input/event3", SdlBackend.KeyOf(FakeSdlRuntime.Pad(1, path: "/dev/input/event3")));
		Assert.Equal("sdl:045E:028E:S1", SdlBackend.KeyOf(new SdlGamepadInfo(1, "Pad", 0x045E, 0x028E, "S1", null, true, false, false)));
		Assert.Equal("sdl:045E:028E:id4", SdlBackend.KeyOf(new SdlGamepadInfo(4, "Pad", 0x045E, 0x028E, null, null, true, false, false)));
	}

	[Fact]
	public async Task AllSdlCalls_ComeFromOneLoopThread()
	{
		var (hub, runtimes) = Create(OnePad);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		runtimes[0].Plug(FakeSdlRuntime.Pad(2, "Second"));
		runtimes[0].Push(SdlEvent.Added(2));
		await Wait.UntilAsync(() => hub.Devices.Count == 2);
		runtimes[0].Push(SdlEvent.Removed(2));
		await Wait.UntilAsync(() => hub.Devices.Count == 1);

		var threads = runtimes[0].LoopThreads.Distinct().ToList();

		Assert.Single(threads);
		Assert.NotEqual(Environment.CurrentManagedThreadId, threads[0]);
	}

	[Fact]
	public async Task AMissingLibrary_MakesTheBackendUnavailable_AndReleasesTheRuntime()
	{
		var (hub, runtimes) = Create(runtime => runtime.InitializeFailure = new DllNotFoundException("SDL3"));
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		await Wait.UntilAsync(() => hub.Backends[0].ErrorCode == InputErrorCode.BackendUnavailable, description: "ошибка недоступности");
		Assert.Equal(BackendState.Unavailable, hub.Backends[0].State);
		Assert.False(string.IsNullOrEmpty(hub.Backends[0].Remediation));
		Assert.True(runtimes[0].Disposed);
	}

	[Fact]
	public async Task AnInitializationErrorWithACode_KeepsItsCode()
	{
		var (hub, _) = Create(runtime => runtime.InitializeFailure =
			new InputException(InputErrorCode.PermissionDenied, "нет доступа к /dev/input") { Remediation = "правила udev" });
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		await Wait.UntilAsync(() => hub.Backends[0].ErrorCode == InputErrorCode.PermissionDenied, description: "код ошибки");
		Assert.Equal("правила udev", hub.Backends[0].Remediation);
	}

	[Fact]
	public async Task AFailureInTheLoop_IsRecoveredByARestart_AndTheDeviceKeepsItsId()
	{
		var (hub, runtimes) = Create(OnePad);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var original = hub.Devices[0].Id;

		runtimes[0].FailNextWait(new IOException("udev исчез"));

		await Wait.UntilAsync(
			() => hub.Backends[0].RestartCount >= 1 && hub.Backends[0].State == BackendState.Running && hub.Devices.Count == 1,
			description: "backend должен перезапуститься");
		Assert.True(runtimes[0].Disposed);
		Assert.True(runtimes.Count >= 2);
		Assert.Equal(original, hub.Devices[0].Id);
	}

	[Fact]
	public async Task Stop_ReleasesTheRuntime_AndLateEventsAreIgnored()
	{
		var (hub, runtimes) = Create(OnePad);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		await hub.StopAsync(TestContext.Current.CancellationToken);

		Assert.True(runtimes[0].Disposed);
		runtimes[0].Push(SdlEvent.ButtonDown(1, 0));
		Assert.Empty(hub.Devices);
	}

	[Fact]
	public async Task PlayHaptic_ReachesTheGamepadWithConvertedValues()
	{
		var (hub, runtimes) = Create(OnePad);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		await hub.PlayHapticAsync(
			hub.Devices[0].Id,
			new HapticEffect(1f, 0f, TimeSpan.FromMilliseconds(300)),
			TestContext.Current.CancellationToken);

		var rumble = Assert.Single(runtimes[0].Rumbles);
		Assert.Equal<uint>(1, rumble.Instance);
		Assert.Equal(ushort.MaxValue, rumble.Low);
		Assert.Equal(0, rumble.High);
		Assert.Equal(300u, rumble.Milliseconds);
	}

	[Fact]
	public async Task PlayHaptic_WithAnInvalidEffect_IsRejectedBeforeReachingSdl()
	{
		var (hub, runtimes) = Create(OnePad);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => hub.PlayHapticAsync(
			hub.Devices[0].Id, new HapticEffect(2f, 0f, TimeSpan.FromSeconds(1)), TestContext.Current.CancellationToken).AsTask());

		Assert.Empty(runtimes[0].Rumbles);
	}

	[Fact]
	public async Task PlayHaptic_WhenTheGamepadCannotRumble_PassesTheReasonThrough()
	{
		var (hub, _) = Create(runtime =>
		{
			OnePad(runtime);
			runtime.RumbleFailure = InputException.Unsupported("Вибрация недоступна для этого геймпада: not supported");
		});
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		var error = await Assert.ThrowsAsync<InputException>(() => hub.PlayHapticAsync(
			hub.Devices[0].Id, new HapticEffect(1f, 1f, TimeSpan.FromSeconds(1)), TestContext.Current.CancellationToken).AsTask());

		Assert.Equal(InputErrorCode.UnsupportedOperation, error.Code);
		Assert.Contains("not supported", error.Message);
	}

	[Fact]
	public async Task PlayHaptic_OnAnUnpluggedGamepad_IsRejected()
	{
		var (hub, runtimes) = Create(OnePad);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var pad = hub.Devices[0].Id;
		runtimes[0].Push(SdlEvent.Removed(1));
		await Wait.UntilAsync(() => hub.Devices.Count == 0);

		await Assert.ThrowsAsync<InputException>(() => hub.PlayHapticAsync(
			pad, new HapticEffect(1f, 1f, TimeSpan.FromSeconds(1)), TestContext.Current.CancellationToken).AsTask());
	}

	[Fact]
	public async Task AnUnavailablePlatform_KeepsTheBackendOutOfRunning_WithoutCreatingARuntime()
	{
		List<FakeSdlRuntime> runtimes = [];
		var backend = new SdlBackend(
			() =>
			{
				var runtime = new FakeSdlRuntime();
				runtimes.Add(runtime);
				return runtime;
			},
			() => BackendProbe.Unavailable(InputErrorCode.BackendUnavailable, "нет SDL3", "установите SDL3"),
			new SdlBackendOptions());
		await using var hub = TestInputHub.Create(backend);

		await hub.StartAsync(TestContext.Current.CancellationToken);

		var status = Assert.Single(hub.Backends);
		Assert.Equal(BackendState.Unavailable, status.State);
		Assert.Equal("установите SDL3", status.Remediation);
		Assert.Empty(runtimes);
	}
}

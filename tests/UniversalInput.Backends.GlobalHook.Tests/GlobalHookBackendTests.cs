using UniversalInput.Backends.GlobalHook.Sources;
using UniversalInput.Backends.GlobalHook.Tests.Fakes;
using UniversalInput.Core.Hub;
using UniversalInput.Testing.Harness;

namespace UniversalInput.Backends.GlobalHook.Tests;

public class GlobalHookBackendTests
{
	private static readonly SubscriptionOptions InputOnly = new()
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

	private static (InputHub Hub, List<FakeHookSource> Sources) Create(Action<FakeHookSource>? configure = null)
	{
		List<FakeHookSource> sources = [];
		var backend = new GlobalHookBackend(() =>
		{
			var source = new FakeHookSource();
			configure?.Invoke(source);
			sources.Add(source);
			return source;
		});

		return (TestInputHub.Create(backend), sources);
	}

	private static HookSignal Key(HookSignalKind kind, int usage, KeyModifiers modifiers = KeyModifiers.None, bool simulated = false) =>
		new() { Kind = kind, Usage = usage, Modifiers = modifiers, Simulated = simulated };

	[Fact]
	public async Task Start_AddsOneAggregateDevice_WithHonestOperations()
	{
		var (hub, _) = Create();
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		var device = Assert.Single(hub.Devices);
		Assert.Equal(InputDeviceKind.SystemWide, device.Kind);
		Assert.Equal(DeviceOrigin.Aggregate, device.Origin);
		Assert.Equal(GlobalHookBackend.BackendId, device.BackendId);
		Assert.True(hub.GetSupport(InputOperation.Observe, device.Id).IsSupported);
		Assert.False(hub.GetSupport(InputOperation.Emulate, device.Id).IsSupported);
		Assert.False(hub.GetSupport(InputOperation.Intercept, device.Id).IsSupported);
		Assert.False(hub.GetSupport(InputOperation.DirectAccess, device.Id).IsSupported);
	}

	[Fact]
	public async Task Keys_ArriveAsAggregateEvents_WithModifiers()
	{
		var (hub, sources) = Create();
		await using var owner = hub;
		await using var subscription = hub.Subscribe(InputOnly);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		sources[0].Raise(Key(HookSignalKind.KeyDown, KeyCodes.A, KeyModifiers.Shift));
		sources[0].Raise(Key(HookSignalKind.KeyUp, KeyCodes.A));
		var events = await subscription.ReadEventsAsync(2);

		Assert.Equal<InputEventKind>([InputEventKind.KeyPressed, InputEventKind.KeyReleased], events.Select(e => e.Kind));
		var pressed = events[0].AsKey();
		Assert.Equal(KeyCodes.A, pressed.Usage);
		Assert.Equal(KeyModifiers.Shift, pressed.Modifiers);
		Assert.False(pressed.IsRepeat);
		Assert.All(events, e => Assert.True((e.Flags & InputEventFlags.Aggregate) != 0));
		Assert.Equal(hub.Devices[0].Id, events[0].Device);
	}

	[Fact]
	public async Task AKeyHeldDown_IsReportedAsRepeat_UntilItIsReleased()
	{
		var (hub, sources) = Create();
		await using var owner = hub;
		await using var subscription = hub.Subscribe(new SubscriptionOptions { Kinds = [InputEventKind.KeyPressed] });
		await hub.StartAsync(TestContext.Current.CancellationToken);

		sources[0].Raise(Key(HookSignalKind.KeyDown, KeyCodes.W));
		sources[0].Raise(Key(HookSignalKind.KeyDown, KeyCodes.W));
		sources[0].Raise(Key(HookSignalKind.KeyUp, KeyCodes.W));
		sources[0].Raise(Key(HookSignalKind.KeyDown, KeyCodes.W));
		var events = await subscription.ReadEventsAsync(3);

		Assert.Equal<bool>([false, true, false], events.Select(e => e.AsKey().IsRepeat));
	}

	[Fact]
	public async Task Mouse_ArrivesWithAbsoluteCoordinates_ButtonsAndWheel()
	{
		var (hub, sources) = Create();
		await using var owner = hub;
		await using var subscription = hub.Subscribe(InputOnly);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		sources[0].Raise(new HookSignal { Kind = HookSignalKind.MouseMove, X = 100, Y = 200 });
		sources[0].Raise(new HookSignal { Kind = HookSignalKind.MouseDown, Button = MouseButton.Right, X = 100, Y = 200 });
		sources[0].Raise(new HookSignal { Kind = HookSignalKind.MouseUp, Button = MouseButton.Right, X = 101, Y = 201 });
		sources[0].Raise(new HookSignal { Kind = HookSignalKind.Wheel, X = 0, Y = 3 });
		var events = await subscription.ReadEventsAsync(4);

		Assert.Equal(new PointerEvent(100, 200, IsAbsolute: true), events[0].AsPointer());
		Assert.Equal(new MouseButtonEvent((int)MouseButton.Right, 100, 200), events[1].AsMouseButton());
		Assert.Equal(InputEventKind.MouseButtonReleased, events[2].Kind);
		Assert.Equal(new WheelEvent(0, 3), events[3].AsWheel());
	}

	[Fact]
	public async Task EventsCreatedByPrograms_AreFlaggedAsSimulated()
	{
		var (hub, sources) = Create();
		await using var owner = hub;
		await using var subscription = hub.Subscribe(InputOnly);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		sources[0].Raise(Key(HookSignalKind.KeyDown, KeyCodes.Q, simulated: true));
		sources[0].Raise(Key(HookSignalKind.KeyUp, KeyCodes.Q));
		var events = await subscription.ReadEventsAsync(2);

		Assert.True((events[0].Flags & InputEventFlags.Simulated) != 0);
		Assert.False((events[1].Flags & InputEventFlags.Simulated) != 0);
	}

	[Fact]
	public async Task AFailedStart_IsReportedAsABackendStatus_AndTheHookIsReleased()
	{
		var (hub, sources) = Create(source => source.StartFailure = new InvalidOperationException("boom"));
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		var expected = OperatingSystem.IsMacOS() ? InputErrorCode.PermissionDenied
			: OperatingSystem.IsLinux() ? InputErrorCode.BackendUnavailable
			: InputErrorCode.BackendFault;
		await Wait.UntilAsync(() => hub.Backends[0].ErrorCode == expected, description: "код ошибки запуска хука");
		Assert.Contains("boom", hub.Backends[0].Reason);
		Assert.True(sources[0].Disposed);
		Assert.False(sources[0].Started);
	}

	[Fact]
	public async Task WhenTheHookStopsByItself_TheBackendRestarts_AndTheDeviceKeepsItsId()
	{
		var (hub, sources) = Create();
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var original = hub.Devices[0].Id;

		sources[0].Fail(new IOException("хук остановился"));

		await Wait.UntilAsync(
			() => hub.Backends[0].RestartCount >= 1 && hub.Backends[0].State == BackendState.Running && hub.Devices.Count == 1,
			description: "backend должен перезапуститься");
		Assert.True(sources[0].Disposed);
		Assert.True(sources.Count >= 2);
		Assert.Equal(original, hub.Devices[0].Id);
	}

	[Fact]
	public async Task Stop_ReleasesTheHook_AndLateSignalsAreIgnored()
	{
		var (hub, sources) = Create();
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		await hub.StopAsync(TestContext.Current.CancellationToken);

		Assert.True(sources[0].Disposed);
		var error = Record.Exception(() => sources[0].Raise(Key(HookSignalKind.KeyDown, KeyCodes.A)));
		Assert.Null(error);
		Assert.Empty(hub.Devices);
	}

	[Fact]
	public async Task AggregateKeys_AreHidden_WhileAPhysicalKeyboardIsObserved()
	{
		List<FakeHookSource> sources = [];
		var hook = new GlobalHookBackend(() =>
		{
			var source = new FakeHookSource();
			sources.Add(source);
			return source;
		});
		var native = new UniversalInput.Testing.Mock.MockInputBackend("native");
		await using var hub = TestInputHub.Create(hook, native);
		await using var plain = hub.Subscribe(new SubscriptionOptions { Kinds = [InputEventKind.KeyPressed] });
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var keyboard = native.Connect(UniversalInput.Testing.Mock.MockInputBackend.Describe("kbd", InputDeviceKind.Keyboard));
		await Wait.UntilAsync(() => hub.Devices.Count == 2, description: "клавиатура должна появиться в реестре");

		sources[0].Raise(Key(HookSignalKind.KeyDown, KeyCodes.A));
		native.Emit(InputEvent.KeyDown(keyboard, KeyCodes.B));
		var first = await plain.ReadEventsAsync(1);
		native.Emit(InputEvent.KeyDown(keyboard, KeyCodes.C));
		var second = await plain.ReadEventsAsync(1);

		Assert.Equal(KeyCodes.B, Assert.Single(first).AsKey().Usage);
		Assert.Equal(KeyCodes.C, Assert.Single(second).AsKey().Usage);
		Assert.False(plain.Reader.TryRead(out _));
	}
}

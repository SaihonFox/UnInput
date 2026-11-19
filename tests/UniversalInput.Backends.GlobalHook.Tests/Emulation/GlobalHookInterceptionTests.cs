using UniversalInput.Backends.GlobalHook.Sources;
using UniversalInput.Backends.GlobalHook.Tests.Fakes;
using UniversalInput.Core.Hub;
using UniversalInput.Testing.Harness;

namespace UniversalInput.Backends.GlobalHook.Tests.Emulation;

public class GlobalHookInterceptionTests
{
	private static (InputHub Hub, List<FakeHookSource> Sources) Create(bool canSuppress)
	{
		List<FakeHookSource> sources = [];
		var backend = new GlobalHookBackend(
			() =>
			{
				var source = new FakeHookSource();
				sources.Add(source);
				return source;
			},
			() => BackendProbe.Available,
			canSuppress: canSuppress);

		return (TestInputHub.Create(backend), sources);
	}

	private static HookSignal Key(HookSignalKind kind, int usage, bool simulated = false) =>
		new() { Kind = kind, Usage = usage, Simulated = simulated };

	private static HookSignal Button(HookSignalKind kind, MouseButton button) =>
		new() { Kind = kind, Button = button };

	[Fact]
	public async Task OnAPlatformWithSuppression_InterceptIsSupported()
	{
		var (hub, _) = Create(canSuppress: true);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		Assert.True(hub.GetSupport(InputOperation.Intercept, hub.Devices[0].Id).IsSupported);
		Assert.True(hub.GetSupport(InputOperation.Intercept).IsSupported);
	}

	[Fact]
	public async Task OnAPlatformWithoutSuppression_InterceptIsUnsupported_AndSetInterceptRuleThrows()
	{
		var (hub, _) = Create(canSuppress: false);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		Assert.False(hub.GetSupport(InputOperation.Intercept, hub.Devices[0].Id).IsSupported);
		var error = Assert.Throws<InputException>(() => hub.SetInterceptRule(new InterceptRule(keys: [KeyCodes.A])));
		Assert.Equal(InputErrorCode.UnsupportedOperation, error.Code);
	}

	[Fact]
	public async Task KeysAndButtonsFromTheRule_AreSuppressed_OthersPass()
	{
		var (hub, sources) = Create(canSuppress: true);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		hub.SetInterceptRule(new InterceptRule(keys: [KeyCodes.A], mouseButtons: [(int)MouseButton.Right]));
		var source = sources[0];

		Assert.True(source.RaiseAndGetSuppressed(Key(HookSignalKind.KeyDown, KeyCodes.A)));
		Assert.True(source.RaiseAndGetSuppressed(Key(HookSignalKind.KeyUp, KeyCodes.A)));
		Assert.False(source.RaiseAndGetSuppressed(Key(HookSignalKind.KeyDown, KeyCodes.B)));
		Assert.True(source.RaiseAndGetSuppressed(Button(HookSignalKind.MouseDown, MouseButton.Right)));
		Assert.True(source.RaiseAndGetSuppressed(Button(HookSignalKind.MouseUp, MouseButton.Right)));
		Assert.False(source.RaiseAndGetSuppressed(Button(HookSignalKind.MouseDown, MouseButton.Left)));
		Assert.False(source.RaiseAndGetSuppressed(new HookSignal { Kind = HookSignalKind.MouseMove, X = 1, Y = 2 }));
	}

	[Fact]
	public async Task ASuppressedEvent_StillReachesSubscribers()
	{
		var (hub, sources) = Create(canSuppress: true);
		await using var owner = hub;
		await using var subscription = hub.Subscribe(new SubscriptionOptions { Kinds = [InputEventKind.KeyPressed] });
		await hub.StartAsync(TestContext.Current.CancellationToken);
		hub.SetInterceptRule(new InterceptRule(keys: [KeyCodes.A]));

		Assert.True(sources[0].RaiseAndGetSuppressed(Key(HookSignalKind.KeyDown, KeyCodes.A)));
		var events = await subscription.ReadEventsAsync(1);

		Assert.Equal(KeyCodes.A, events[0].AsKey().Usage);
	}

	[Fact]
	public async Task SimulatedEvents_AreNeverSuppressed()
	{
		var (hub, sources) = Create(canSuppress: true);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		hub.SetInterceptRule(new InterceptRule(keys: [KeyCodes.A]));

		Assert.False(sources[0].RaiseAndGetSuppressed(Key(HookSignalKind.KeyDown, KeyCodes.A, simulated: true)));
	}

	[Fact]
	public async Task ANullRule_StopsSuppression()
	{
		var (hub, sources) = Create(canSuppress: true);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		hub.SetInterceptRule(new InterceptRule(keys: [KeyCodes.A]));
		hub.SetInterceptRule(null);

		Assert.False(sources[0].RaiseAndGetSuppressed(Key(HookSignalKind.KeyDown, KeyCodes.A)));
	}

	[Fact]
	public async Task TheRule_SurvivesARestartOfTheHook()
	{
		var (hub, sources) = Create(canSuppress: true);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		hub.SetInterceptRule(new InterceptRule(keys: [KeyCodes.A]));

		sources[0].Fail(new InvalidOperationException("хук упал"));
		await Wait.UntilAsync(() => sources.Count == 2);

		Assert.True(sources[1].RaiseAndGetSuppressed(Key(HookSignalKind.KeyDown, KeyCodes.A)));
	}

	[Fact]
	public async Task AfterStop_TheSourceNoLongerAsksTheRule()
	{
		var (hub, sources) = Create(canSuppress: true);
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		hub.SetInterceptRule(new InterceptRule(keys: [KeyCodes.A]));

		await hub.StopAsync(TestContext.Current.CancellationToken);

		Assert.Null(sources[0].ShouldSuppress);
	}
}

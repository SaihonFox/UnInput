using UniversalInput.Backends.GlobalHook.Emulation;
using UniversalInput.Backends.GlobalHook.Tests.Fakes;
using UniversalInput.Core.Hub;
using UniversalInput.Testing.Harness;

namespace UniversalInput.Backends.GlobalHook.Tests.Emulation;

public class GlobalHookEmulationTests
{
	private static (InputHub Hub, GlobalHookBackend Backend, List<FakeHookEmulator> Emulators) Create()
	{
		List<FakeHookEmulator> emulators = [];
		var backend = new GlobalHookBackend(
			() => new FakeHookSource(),
			() => BackendProbe.Available,
			() =>
			{
				var emulator = new FakeHookEmulator();
				emulators.Add(emulator);
				return emulator;
			},
			canSuppress: false);

		return (TestInputHub.Create(backend), backend, emulators);
	}

	[Fact]
	public async Task Start_DeclaresEmulationSupported_ButNotInterceptionOrDirectAccess()
	{
		var (hub, _, _) = Create();
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		var device = Assert.Single(hub.Devices);
		Assert.True(hub.GetSupport(InputOperation.Emulate, device.Id).IsSupported);
		Assert.True(hub.GetSupport(InputOperation.Emulate).IsSupported);
		Assert.False(hub.GetSupport(InputOperation.Intercept, device.Id).IsSupported);
		Assert.False(hub.GetSupport(InputOperation.DirectAccess, device.Id).IsSupported);
	}

	[Fact]
	public async Task HubEmulation_RoutesEveryKindOfEventToTheEmulator()
	{
		var (hub, _, emulators) = Create();
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var device = hub.Devices[0].Id;
		var token = TestContext.Current.CancellationToken;

		await hub.EmulateAsync(InputEvent.KeyDown(device, KeyCodes.A), token);
		await hub.EmulateAsync(InputEvent.KeyUp(device, KeyCodes.A), token);
		await hub.EmulateAsync(InputEvent.MouseMove(device, 10, 20, absolute: true), token);
		await hub.EmulateAsync(InputEvent.MouseMove(device, -3, 4), token);
		await hub.EmulateAsync(InputEvent.MouseDown(device, (int)MouseButton.Right), token);
		await hub.EmulateAsync(InputEvent.MouseUp(device, (int)MouseButton.Right), token);
		await hub.EmulateAsync(InputEvent.Wheel(device, 1, -2), token);

		var emulator = Assert.Single(emulators);
		Assert.Equal<string>(
			["down:04", "up:04", "move:abs:10,20", "move:rel:-3,4", "button:Right:down", "button:Right:up", "wheel:1,-2"],
			emulator.Calls);
	}

	[Fact]
	public async Task HubEmulation_WithoutADevice_UsesTheBackendThatCanEmulate()
	{
		var (hub, _, emulators) = Create();
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		await hub.EmulateAsync(InputEvent.KeyDown(default, KeyCodes.Space), TestContext.Current.CancellationToken);

		Assert.Equal<string>(["down:2C"], Assert.Single(emulators).Calls);
	}

	[Fact]
	public async Task TheEmulator_IsCreatedOnFirstUse_AndOnlyOnce()
	{
		var (hub, _, emulators) = Create();
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		Assert.Empty(emulators);

		await hub.EmulateAsync(InputEvent.KeyDown(default, KeyCodes.A), TestContext.Current.CancellationToken);
		await hub.EmulateAsync(InputEvent.KeyUp(default, KeyCodes.A), TestContext.Current.CancellationToken);

		Assert.Single(emulators);
	}

	[Fact]
	public async Task EventsThatAreNotKeyboardOrMouse_AreRejected_WithoutTouchingTheEmulator()
	{
		var (hub, _, emulators) = Create();
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		var error = await Assert.ThrowsAsync<InputException>(() => hub.EmulateAsync(
			InputEvent.GamepadDown(default, GamepadButton.South), TestContext.Current.CancellationToken).AsTask());

		Assert.Equal(InputErrorCode.UnsupportedOperation, error.Code);
		Assert.All(emulators, emulator => Assert.Empty(emulator.Calls));
	}

	[Fact]
	public async Task AnUnknownMouseButton_IsRejectedAsUnsupported()
	{
		var (hub, _, emulators) = Create();
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		var error = await Assert.ThrowsAsync<InputException>(() => hub.EmulateAsync(
			InputEvent.MouseDown(default, 99), TestContext.Current.CancellationToken).AsTask());

		Assert.Equal(InputErrorCode.UnsupportedOperation, error.Code);
		Assert.All(emulators, emulator => Assert.Empty(emulator.Calls));
	}

	[Theory]
	[InlineData("Unsupported", InputErrorCode.UnsupportedOperation)]
	[InlineData("UnknownKey", InputErrorCode.UnsupportedOperation)]
	[InlineData("AccessibilityDenied", InputErrorCode.PermissionDenied)]
	[InlineData("UinputUnavailable", InputErrorCode.PermissionDenied)]
	[InlineData("DisplayUnavailable", InputErrorCode.BackendUnavailable)]
	[InlineData("Failed", InputErrorCode.BackendFault)]
	public async Task AFailedEmulation_BecomesAnInputException_WithTheMatchingCode(string status, InputErrorCode expected)
	{
		var (hub, _, emulators) = Create();
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		// Эмулятор создаётся при первой команде; первая успешна, чтобы получить доступ к нему.
		await hub.EmulateAsync(InputEvent.KeyDown(default, KeyCodes.A), TestContext.Current.CancellationToken);
		emulators[0].Result = Enum.Parse<EmulationStatus>(status);

		var error = await Assert.ThrowsAsync<InputException>(() => hub.EmulateAsync(
			InputEvent.KeyUp(default, KeyCodes.A), TestContext.Current.CancellationToken).AsTask());

		Assert.Equal(expected, error.Code);
	}

	[Fact]
	public async Task PermissionErrors_CarryARemediation()
	{
		var (hub, _, emulators) = Create();
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		await hub.EmulateAsync(InputEvent.KeyDown(default, KeyCodes.A), TestContext.Current.CancellationToken);
		emulators[0].Result = EmulationStatus.AccessibilityDenied;

		var error = await Assert.ThrowsAsync<InputException>(() => hub.EmulateAsync(
			InputEvent.KeyUp(default, KeyCodes.A), TestContext.Current.CancellationToken).AsTask());

		Assert.False(string.IsNullOrWhiteSpace(error.Remediation));
	}

	[Fact]
	public async Task AnEmulatorThatCannotBeCreated_IsReportedAsBackendUnavailable()
	{
		var backend = new GlobalHookBackend(
			() => new FakeHookSource(),
			() => BackendProbe.Available,
			() => throw new DllNotFoundException("libuiohook"));
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		var error = await Assert.ThrowsAsync<InputException>(() => hub.EmulateAsync(
			InputEvent.KeyDown(default, KeyCodes.A), TestContext.Current.CancellationToken).AsTask());

		Assert.Equal(InputErrorCode.BackendUnavailable, error.Code);
	}

	[Fact]
	public async Task BeforeStart_EmulationIsUnavailable()
	{
		var backend = new GlobalHookBackend(() => new FakeHookSource(), () => BackendProbe.Available, () => new FakeHookEmulator());
		Assert.True(backend.TryGetFeature<IInputEmulator>(out var emulator));

		var error = await Assert.ThrowsAsync<InputException>(() => emulator.EmulateAsync(
			InputEvent.KeyDown(default, KeyCodes.A), TestContext.Current.CancellationToken).AsTask());

		Assert.Equal(InputErrorCode.DeviceUnavailable, error.Code);
	}

	[Fact]
	public async Task Stop_ReleasesTheEmulator_AndFurtherEmulationFails()
	{
		var (hub, backend, emulators) = Create();
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		await hub.EmulateAsync(InputEvent.KeyDown(default, KeyCodes.A), TestContext.Current.CancellationToken);
		Assert.True(backend.TryGetFeature<IInputEmulator>(out var emulator));

		await hub.StopAsync(TestContext.Current.CancellationToken);

		Assert.True(Assert.Single(emulators).Disposed);
		var error = await Assert.ThrowsAsync<InputException>(() => emulator.EmulateAsync(
			InputEvent.KeyUp(default, KeyCodes.A), TestContext.Current.CancellationToken).AsTask());
		Assert.Equal(InputErrorCode.DeviceUnavailable, error.Code);
	}

	[Fact]
	public async Task ACancelledToken_StopsEmulationBeforeTheEmulatorIsCalled()
	{
		var (hub, backend, emulators) = Create();
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		Assert.True(backend.TryGetFeature<IInputEmulator>(out var emulator));
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => emulator.EmulateAsync(
			InputEvent.KeyDown(default, KeyCodes.A), cts.Token).AsTask());

		Assert.Empty(emulators);
	}
}

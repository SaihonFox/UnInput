using UniversalInput.Core.Backends;
using UniversalInput.Core.Hub;
using UniversalInput.Testing.Harness;
using UniversalInput.Testing.Mock;

namespace UniversalInput.Core.Tests.Hub;

public class InputHubOperationTests
{
	private static MockInputBackend WithKeyboard(string id = "mock")
	{
		var backend = new MockInputBackend(id);
		backend.InitialDevices.Add(MockInputBackend.Describe("kbd", InputDeviceKind.Keyboard));
		return backend;
	}

	private static MockInputBackend Unavailable(InputErrorCode code, string reason, string remediation) =>
		new("denied")
		{
			ProbeResult = BackendProbe.Unavailable(code, reason, remediation),
		};

	// ---- GetSupport ----

	[Fact]
	public async Task GetSupport_ReflectsTheFeaturesOfRunningBackends()
	{
		await using var hub = TestInputHub.Create(WithKeyboard());
		await hub.StartAsync();

		Assert.True(hub.GetSupport(InputOperation.Enumerate).IsSupported);
		Assert.True(hub.GetSupport(InputOperation.Observe).IsSupported);
		Assert.True(hub.GetSupport(InputOperation.Emulate).IsSupported);
		Assert.True(hub.GetSupport(InputOperation.DirectAccess).IsSupported);
		Assert.Equal(SupportState.Unsupported, hub.GetSupport(InputOperation.Intercept).State);
		Assert.Equal(SupportState.Unsupported, hub.GetSupport(InputOperation.Output).State);
	}

	[Fact]
	public async Task GetSupport_ForADevice_UsesWhatTheBackendDeclared()
	{
		var backend = new MockInputBackend();
		backend.InitialDevices.Add(new DeviceDescriptor
		{
			Key = new DeviceKey("pad"),
			Name = "pad",
			Kind = InputDeviceKind.Gamepad,
			Operations = new Dictionary<InputOperation, OperationSupport>
			{
				[InputOperation.Observe] = OperationSupport.Supported,
				[InputOperation.Emulate] = new OperationSupport(
					SupportState.RestrictedByPlatform, "эмуляция геймпада запрещена", null),
			},
		});
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync();
		var pad = backend.IdOf("pad");

		Assert.True(hub.GetSupport(InputOperation.Observe, pad).IsSupported);
		Assert.Equal(SupportState.RestrictedByPlatform, hub.GetSupport(InputOperation.Emulate, pad).State);
		Assert.Equal(SupportState.Unsupported, hub.GetSupport(InputOperation.DirectAccess, pad).State);
		Assert.True(hub.GetSupport(InputOperation.Enumerate, pad).IsSupported);
	}

	[Fact]
	public async Task GetSupport_ForAnUnknownDevice_IsUnsupported()
	{
		await using var hub = TestInputHub.Create(WithKeyboard());
		await hub.StartAsync();

		Assert.Equal(SupportState.Unsupported, hub.GetSupport(InputOperation.Observe, new DeviceId(404)).State);
	}

	[Fact]
	public async Task GetSupport_WhenABackendNeedsPermission_ExplainsHowToFixIt()
	{
		await using var hub = TestInputHub.Create(
			Unavailable(InputErrorCode.PermissionDenied, "нужен доступ к /dev/uinput", "добавьте правило udev"));
		await hub.StartAsync();

		var support = hub.GetSupport(InputOperation.Emulate);

		Assert.Equal(SupportState.RequiresPermission, support.State);
		Assert.Equal("нужен доступ к /dev/uinput", support.Reason);
		Assert.Equal("добавьте правило udev", support.Remediation);
	}

	[Fact]
	public async Task GetSupport_PrefersARunningBackendOverAFailedOne()
	{
		await using var hub = TestInputHub.Create(
			Unavailable(InputErrorCode.PermissionDenied, "нет прав", "выдайте права"),
			WithKeyboard());
		await hub.StartAsync();

		Assert.True(hub.GetSupport(InputOperation.Emulate).IsSupported);
	}

	[Fact]
	public async Task GetSupport_WithoutBackends_IsUnsupported()
	{
		await using var hub = TestInputHub.Create();
		await hub.StartAsync();

		Assert.Equal(SupportState.Unsupported, hub.GetSupport(InputOperation.Observe).State);
	}

	// ---- EmulateAsync ----

	[Fact]
	public async Task Emulate_ForwardsTheEventToTheBackend_MarkedAsSimulated()
	{
		var backend = WithKeyboard();
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync();

		await hub.EmulateAsync(InputEvent.KeyDown(DeviceId.None, KeyCodes.A));

		var emulated = Assert.Single(backend.Emulated);
		Assert.Equal(KeyCodes.A, emulated.AsKey().Usage);
		Assert.True(emulated.IsSimulated);
	}

	[Fact]
	public async Task Emulate_ThroughASpecificDevice_UsesTheBackendThatOwnsIt()
	{
		var first = new MockInputBackend("first");
		var second = WithKeyboard("second");
		await using var hub = TestInputHub.Create(first, second);
		await hub.StartAsync();

		await hub.EmulateAsync(InputEvent.KeyDown(second.IdOf("kbd"), KeyCodes.B));

		Assert.Empty(first.Emulated);
		Assert.Single(second.Emulated);
	}

	[Fact]
	public async Task Emulate_WithoutSupport_ThrowsUnsupportedOperation()
	{
		var backend = WithKeyboard();
		backend.EmulationEnabled = false;
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync();

		var error = await Assert.ThrowsAsync<InputException>(
			() => hub.EmulateAsync(InputEvent.KeyDown(DeviceId.None, 1)).AsTask());

		Assert.Equal(InputErrorCode.UnsupportedOperation, error.Code);
		Assert.Empty(backend.Emulated);
	}

	[Fact]
	public async Task Emulate_WhenPermissionIsMissing_ThrowsPermissionDeniedWithRemediation()
	{
		await using var hub = TestInputHub.Create(
			Unavailable(InputErrorCode.PermissionDenied, "нужны права Accessibility", "включите в настройках macOS"));
		await hub.StartAsync();

		var error = await Assert.ThrowsAsync<InputException>(
			() => hub.EmulateAsync(InputEvent.KeyDown(DeviceId.None, 1)).AsTask());

		Assert.Equal(InputErrorCode.PermissionDenied, error.Code);
		Assert.Equal("включите в настройках macOS", error.Remediation);
	}

	[Fact]
	public async Task Emulate_WhenThePlatformForbidsIt_ThrowsPlatformRestriction()
	{
		await using var hub = TestInputHub.Create(
			Unavailable(InputErrorCode.PlatformRestriction, "Wayland не даёт абсолютные координаты", "используйте X11"));
		await hub.StartAsync();

		var error = await Assert.ThrowsAsync<InputException>(
			() => hub.EmulateAsync(InputEvent.MouseMove(DeviceId.None, 1, 1, absolute: true)).AsTask());

		Assert.Equal(InputErrorCode.PlatformRestriction, error.Code);
	}

	[Fact]
	public async Task Emulate_ForAMissingDevice_ThrowsDeviceUnavailable()
	{
		await using var hub = TestInputHub.Create(WithKeyboard());
		await hub.StartAsync();

		var error = await Assert.ThrowsAsync<InputException>(
			() => hub.EmulateAsync(InputEvent.KeyDown(new DeviceId(404), 1)).AsTask());

		Assert.Equal(InputErrorCode.DeviceUnavailable, error.Code);
	}

	[Fact]
	public async Task Emulate_PicksTheBackendByConfiguredPriority()
	{
		var first = new MockInputBackend("first");
		var second = new MockInputBackend("second");
		var options = TestInputHub.FastOptions();
		options.BackendPriority = ["second", "first"];
		await using var hub = TestInputHub.Create(options, first, second);
		await hub.StartAsync();

		await hub.EmulateAsync(InputEvent.KeyDown(DeviceId.None, 1));

		Assert.Empty(first.Emulated);
		Assert.Single(second.Emulated);
	}

	[Fact]
	public async Task Emulate_WithoutPriority_UsesRegistrationOrder()
	{
		var first = new MockInputBackend("first");
		var second = new MockInputBackend("second");
		await using var hub = TestInputHub.Create(first, second);
		await hub.StartAsync();

		await hub.EmulateAsync(InputEvent.KeyDown(DeviceId.None, 1));

		Assert.Single(first.Emulated);
		Assert.Empty(second.Emulated);
	}

	[Fact]
	public async Task Emulate_WithACancelledToken_Throws()
	{
		await using var hub = TestInputHub.Create(WithKeyboard());
		await hub.StartAsync();
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => hub.EmulateAsync(InputEvent.KeyDown(DeviceId.None, 1), cts.Token).AsTask());
	}

	// ---- OpenRawAsync ----

	[Fact]
	public async Task OpenRaw_ReadsAndWritesReports()
	{
		var backend = WithKeyboard();
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync();
		var device = backend.IdOf("kbd");

		await using var channel = await hub.OpenRawAsync(device, RawAccessMode.ReadWrite);
		backend.GetChannel(device)!.Push(1, 2, 3);
		var buffer = new byte[8];
		var length = await channel.ReadAsync(buffer, CancellationToken.None);
		byte[] report = [9, 8];
		await channel.WriteAsync(report, CancellationToken.None);

		byte[] expectedRead = [1, 2, 3];
		Assert.Equal(3, length);
		Assert.Equal(expectedRead, buffer.Take(length));
		Assert.Equal(report, Assert.Single(backend.GetChannel(device)!.Written));
	}

	[Fact]
	public async Task OpenRaw_SecondOpenOfTheSameDevice_ThrowsDeviceBusy_UntilTheFirstIsClosed()
	{
		var backend = WithKeyboard();
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync();
		var device = backend.IdOf("kbd");

		var first = await hub.OpenRawAsync(device, RawAccessMode.Read);
		var busy = await Assert.ThrowsAsync<InputException>(
			() => hub.OpenRawAsync(device, RawAccessMode.Read).AsTask());
		await first.DisposeAsync();
		await using var second = await hub.OpenRawAsync(device, RawAccessMode.Read);

		Assert.Equal(InputErrorCode.DeviceBusy, busy.Code);
		Assert.NotNull(second);
	}

	[Fact]
	public async Task OpenRaw_WhenTheDeviceDisappearsDuringRead_FailsWithDeviceUnavailable()
	{
		var backend = WithKeyboard();
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync();
		var device = backend.IdOf("kbd");
		var channel = await hub.OpenRawAsync(device, RawAccessMode.Read);

		var pending = channel.ReadAsync(new byte[8], CancellationToken.None).AsTask();
		backend.Disconnect(device);

		var error = await Assert.ThrowsAsync<InputException>(() => pending);
		Assert.Equal(InputErrorCode.DeviceUnavailable, error.Code);

		var later = await Assert.ThrowsAsync<InputException>(
			() => channel.ReadAsync(new byte[8], CancellationToken.None).AsTask());
		Assert.Equal(InputErrorCode.DeviceUnavailable, later.Code);
		Assert.True(backend.GetChannel(device)!.IsDisposed);
	}

	[Fact]
	public async Task OpenRaw_WithoutSupport_ThrowsUnsupportedOperation()
	{
		var backend = WithKeyboard();
		backend.RawAccessEnabled = false;
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync();

		var error = await Assert.ThrowsAsync<InputException>(
			() => hub.OpenRawAsync(backend.IdOf("kbd"), RawAccessMode.Read).AsTask());

		Assert.Equal(InputErrorCode.UnsupportedOperation, error.Code);
	}

	[Fact]
	public async Task OpenRaw_ForAMissingDevice_ThrowsDeviceUnavailable()
	{
		await using var hub = TestInputHub.Create(WithKeyboard());
		await hub.StartAsync();

		var error = await Assert.ThrowsAsync<InputException>(
			() => hub.OpenRawAsync(new DeviceId(404), RawAccessMode.Read).AsTask());

		Assert.Equal(InputErrorCode.DeviceUnavailable, error.Code);
	}

	[Fact]
	public async Task OpenRaw_WithoutADevice_ThrowsArgumentException()
	{
		await using var hub = TestInputHub.Create(WithKeyboard());
		await hub.StartAsync();

		await Assert.ThrowsAsync<ArgumentException>(
			() => hub.OpenRawAsync(DeviceId.None, RawAccessMode.Read).AsTask());
	}
}

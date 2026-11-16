using UniversalInput.Backends.Hid.Discovery;
using UniversalInput.Backends.Hid.Options;
using UniversalInput.Backends.Hid.Tests.Fakes;
using UniversalInput.Core.Hub;
using UniversalInput.Testing.Harness;

namespace UniversalInput.Backends.Hid.Tests;

public class HidBackendTests
{
	/// <summary>Периодический пересчёт по умолчанию отодвинут: в тестах список обновляет событие смены.</summary>
	private static (InputHub Hub, FakeHidPlatform Platform) Create(
		Action<HidBackendOptions>? configure = null, params HidDeviceInfo[] devices)
	{
		var platform = new FakeHidPlatform();
		platform.SetDevices(devices);
		var options = new HidBackendOptions { RescanInterval = TimeSpan.FromMinutes(5) };
		configure?.Invoke(options);
		return (TestInputHub.Create(new HidBackend(platform, options)), platform);
	}

	private static DeviceId IdOf(InputHub hub, string path) => hub.Devices.Single(d => d.Identity?.Path == path).Id;

	[Fact]
	public async Task Start_ExposesHidInterfaces_WithIdentityKindAndHonestOperations()
	{
		var (hub, _) = Create(null, FakeHidPlatform.Keyboard("kb0"), FakeHidPlatform.Vendor("v0"));
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		Assert.Equal(2, hub.Devices.Count);
		var keyboard = hub.Devices.Single(d => d.Identity?.Path == "kb0");
		Assert.Equal("Keyboard", keyboard.Name);
		Assert.Equal(InputDeviceKind.Keyboard, keyboard.Kind);
		Assert.Equal(DeviceBus.Unknown, keyboard.Bus);
		Assert.Equal(InputCapabilities.RawReports | InputCapabilities.Keys, keyboard.Capabilities);
		Assert.Equal((ushort)0x046D, keyboard.Identity?.VendorId);
		Assert.Equal((ushort)0xC31C, keyboard.Identity?.ProductId);
		Assert.Equal("SN1", keyboard.Identity?.SerialNumber);
		Assert.Equal("0001:0006", keyboard.Properties["usages"]);
		Assert.Equal("HidSharp", keyboard.Properties["library"]);
		Assert.Equal(SupportState.Supported, keyboard.Operations[InputOperation.DirectAccess].State);
		Assert.True(hub.GetSupport(InputOperation.DirectAccess, keyboard.Id).IsSupported);
		Assert.False(hub.GetSupport(InputOperation.Observe, keyboard.Id).IsSupported);
		Assert.False(hub.GetSupport(InputOperation.Emulate, keyboard.Id).IsSupported);

		var vendor = hub.Devices.Single(d => d.Identity?.Path == "v0");
		Assert.Equal("HID 1234:0001", vendor.Name);
		Assert.Equal(InputDeviceKind.Hid, vendor.Kind);
		Assert.Null(vendor.Identity?.SerialNumber);

		var status = Assert.Single(hub.Backends);
		Assert.Equal(HidBackend.BackendId, status.Id);
		Assert.Contains(InputOperation.DirectAccess, status.Roles);
		Assert.DoesNotContain(InputOperation.Emulate, status.Roles);
	}

	[Fact]
	public async Task Filter_HidesDevicesThatDoNotMatch()
	{
		var (hub, _) = Create(
			options => options.Filter = info => info.VendorId == 0x1234,
			FakeHidPlatform.Keyboard("kb0"),
			FakeHidPlatform.Vendor("v0"));
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		Assert.Equal("v0", Assert.Single(hub.Devices).Identity?.Path);
	}

	[Fact]
	public async Task TheSameInterfaceListedTwice_IsOneDevice()
	{
		var (hub, _) = Create(null, FakeHidPlatform.Keyboard("kb0"), FakeHidPlatform.Keyboard("kb0"));
		await using var owner = hub;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		Assert.Single(hub.Devices);
	}

	[Fact]
	public async Task HotPlug_IsDetectedFromTheChangeNotification()
	{
		var (hub, platform) = Create(null, FakeHidPlatform.Keyboard("kb0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		platform.SetDevices(FakeHidPlatform.Keyboard("kb0"), FakeHidPlatform.Vendor("v0"));
		platform.RaiseChanged();
		await Wait.UntilAsync(() => hub.Devices.Count == 2, description: "v0 должен появиться");

		platform.SetDevices(FakeHidPlatform.Vendor("v0"));
		platform.RaiseChanged();
		await Wait.UntilAsync(() => hub.Devices.Count == 1, description: "kb0 должен исчезнуть");

		Assert.Equal("v0", Assert.Single(hub.Devices).Identity?.Path);
	}

	[Fact]
	public async Task RepeatedNotifications_DoNotDuplicateDevices()
	{
		var (hub, platform) = Create(null, FakeHidPlatform.Keyboard("kb0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var original = IdOf(hub, "kb0");

		platform.RaiseChanged();
		platform.RaiseChanged();
		platform.RaiseChanged();
		platform.SetDevices(FakeHidPlatform.Keyboard("kb0"), FakeHidPlatform.Vendor("v0"));
		platform.RaiseChanged();
		await Wait.UntilAsync(() => hub.Devices.Count == 2);

		Assert.Equal(2, hub.Devices.Select(d => d.Key).Distinct().Count());
		Assert.Equal(original, IdOf(hub, "kb0"));
	}

	[Fact]
	public async Task WithoutNotifications_ThePeriodicRescanFindsChanges()
	{
		var (hub, platform) = Create(options => options.RescanInterval = TimeSpan.FromMilliseconds(20));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		platform.SetDevices(FakeHidPlatform.Keyboard("kb0"));

		await Wait.UntilAsync(() => hub.Devices.Count == 1, description: "пересчёт по таймеру");
	}

	[Fact]
	public async Task AnInterfaceThatReturns_KeepsItsDeviceId()
	{
		var (hub, platform) = Create(null, FakeHidPlatform.Keyboard("kb0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var original = IdOf(hub, "kb0");

		platform.SetDevices();
		platform.RaiseChanged();
		await Wait.UntilAsync(() => hub.Devices.Count == 0);
		platform.SetDevices(FakeHidPlatform.Keyboard("kb0"));
		platform.RaiseChanged();
		await Wait.UntilAsync(() => hub.Devices.Count == 1);

		Assert.Equal(original, IdOf(hub, "kb0"));
	}

	[Fact]
	public async Task OpenRaw_ReadsAndWritesReportsAsIs()
	{
		var (hub, platform) = Create(null, FakeHidPlatform.Keyboard("kb0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		await using var channel = await hub.OpenRawAsync(IdOf(hub, "kb0"), RawAccessMode.ReadWrite, TestContext.Current.CancellationToken);
		var connection = platform.Connections["kb0"];
		byte[] input = [0, 0, 0x04, 0, 0, 0, 0, 0];
		connection.Push(input);
		var buffer = new byte[8];
		var length = await channel.ReadAsync(buffer, TestContext.Current.CancellationToken);
		byte[] output = [0x02, 0x01];
		await channel.WriteAsync(output, TestContext.Current.CancellationToken);

		Assert.Equal<byte>(input, buffer.Take(length));
		Assert.Equal<byte>(output, Assert.Single(connection.Written));
	}

	[Fact]
	public async Task OpenRaw_WithoutReadMode_RejectsReading()
	{
		var (hub, _) = Create(null, FakeHidPlatform.Keyboard("kb0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		await using var channel = await hub.OpenRawAsync(IdOf(hub, "kb0"), RawAccessMode.Write, TestContext.Current.CancellationToken);
		var error = await Assert.ThrowsAsync<InputException>(
			() => channel.ReadAsync(new byte[8], TestContext.Current.CancellationToken).AsTask());

		Assert.Equal(InputErrorCode.UnsupportedOperation, error.Code);
	}

	[Fact]
	public async Task OpenRaw_WithoutWriteMode_RejectsWriting()
	{
		var (hub, _) = Create(null, FakeHidPlatform.Keyboard("kb0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		await using var channel = await hub.OpenRawAsync(IdOf(hub, "kb0"), RawAccessMode.Read, TestContext.Current.CancellationToken);
		var error = await Assert.ThrowsAsync<InputException>(
			() => channel.WriteAsync(new byte[] { 0, 1 }, TestContext.Current.CancellationToken).AsTask());

		Assert.Equal(InputErrorCode.UnsupportedOperation, error.Code);
	}

	[Fact]
	public async Task ABufferShorterThanTheLongestInputReport_IsRejectedInsteadOfTruncating()
	{
		var (hub, _) = Create(null, FakeHidPlatform.Keyboard("kb0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);

		await using var channel = await hub.OpenRawAsync(IdOf(hub, "kb0"), RawAccessMode.Read, TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<ArgumentException>(
			() => channel.ReadAsync(new byte[4], TestContext.Current.CancellationToken).AsTask());
	}

	[Fact]
	public async Task UnpluggingAnOpenDevice_FailsPendingReadWithDeviceUnavailable()
	{
		var (hub, platform) = Create(null, FakeHidPlatform.Keyboard("kb0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var channel = await hub.OpenRawAsync(IdOf(hub, "kb0"), RawAccessMode.Read, TestContext.Current.CancellationToken);
		var connection = platform.Connections["kb0"];

		var pending = channel.ReadAsync(new byte[8], CancellationToken.None).AsTask();
		connection.Fail(new IOException("device removed"));
		platform.SetDevices();
		platform.RaiseChanged();

		var error = await Assert.ThrowsAsync<InputException>(() => pending);
		Assert.Equal(InputErrorCode.DeviceUnavailable, error.Code);
		await Wait.UntilAsync(() => hub.Devices.Count == 0);
		await Wait.UntilAsync(() => connection.IsDisposed, description: "канал должен быть закрыт");
	}

	[Fact]
	public async Task OpenRaw_WhenTheOsDeniesAccess_ReportsPermissionDeniedWithAHint()
	{
		var (hub, platform) = Create(null, FakeHidPlatform.Keyboard("kb0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		platform.OpenFailure = new UnauthorizedAccessException("denied");

		var error = await Assert.ThrowsAsync<InputException>(
			() => hub.OpenRawAsync(IdOf(hub, "kb0"), RawAccessMode.ReadWrite, TestContext.Current.CancellationToken).AsTask());

		Assert.Equal(InputErrorCode.PermissionDenied, error.Code);
		Assert.False(string.IsNullOrEmpty(error.Remediation));
	}

	[Fact]
	public async Task OpenRaw_WhenTheDeviceCannotBeOpened_ReportsBusyWithAHint()
	{
		var (hub, platform) = Create(null, FakeHidPlatform.Keyboard("kb0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		platform.OpenFailure = new IOException("Unable to open HID class device");

		var error = await Assert.ThrowsAsync<InputException>(
			() => hub.OpenRawAsync(IdOf(hub, "kb0"), RawAccessMode.ReadWrite, TestContext.Current.CancellationToken).AsTask());

		Assert.Equal(InputErrorCode.DeviceBusy, error.Code);
		Assert.False(string.IsNullOrEmpty(error.Remediation));
		Assert.Contains("Unable to open", error.Message);
	}

	[Fact]
	public async Task ARescanFailure_IsRecoveredByARestart_AndDevicesKeepTheirIds()
	{
		var (hub, platform) = Create(null, FakeHidPlatform.Keyboard("kb0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var original = IdOf(hub, "kb0");

		platform.GetDevicesFailure = new IOException("udev error");
		platform.FailuresRemaining = 1;
		platform.RaiseChanged();

		await Wait.UntilAsync(
			() => hub.Backends[0].RestartCount >= 1 && hub.Backends[0].State == BackendState.Running && hub.Devices.Count == 1,
			description: "backend должен перезапуститься");
		Assert.Equal(original, IdOf(hub, "kb0"));
	}

	[Fact]
	public async Task AMissingNativeLibrary_MakesTheBackendUnavailableWithAHint()
	{
		var (hub, platform) = Create();
		await using var owner = hub;
		platform.GetDevicesFailure = new DllNotFoundException("libudev.so.1");
		platform.FailuresRemaining = int.MaxValue;

		await hub.StartAsync(TestContext.Current.CancellationToken);

		await Wait.UntilAsync(() => hub.Backends[0].ErrorCode == InputErrorCode.BackendUnavailable, description: "ошибка недоступности");
		Assert.Equal(BackendState.Unavailable, hub.Backends[0].State);
		Assert.False(string.IsNullOrEmpty(hub.Backends[0].Remediation));
	}

	[Fact]
	public async Task Stop_UnsubscribesFromTheChangeNotifications()
	{
		var (hub, platform) = Create(null, FakeHidPlatform.Keyboard("kb0"));
		await using var owner = hub;
		await hub.StartAsync(TestContext.Current.CancellationToken);
		Assert.Equal(1, platform.SubscriberCount);

		await hub.StopAsync(TestContext.Current.CancellationToken);

		Assert.Equal(0, platform.SubscriberCount);
		Assert.Empty(hub.Devices);
	}

	[Fact]
	public async Task Probe_OfAHealthyPlatform_IsAvailable()
	{
		var backend = new HidBackend(new FakeHidPlatform(), new HidBackendOptions());

		var probe = await backend.ProbeAsync(TestContext.Current.CancellationToken);

		Assert.True(probe.IsAvailable);
	}

	[Fact]
	public async Task Probe_WithoutTheNativeLibrary_ExplainsHowToFixIt()
	{
		var platform = new FakeHidPlatform { GetDevicesFailure = new DllNotFoundException("libudev.so.1"), FailuresRemaining = 1 };
		var backend = new HidBackend(platform, new HidBackendOptions());

		var probe = await backend.ProbeAsync(TestContext.Current.CancellationToken);

		Assert.False(probe.IsAvailable);
		Assert.Equal(InputErrorCode.BackendUnavailable, probe.FailureCode);
		Assert.False(string.IsNullOrEmpty(probe.Remediation));
	}

	[Fact]
	public async Task Probe_AfterAnotherFailure_IsAFault()
	{
		var platform = new FakeHidPlatform { GetDevicesFailure = new InvalidOperationException("boom"), FailuresRemaining = 1 };
		var backend = new HidBackend(platform, new HidBackendOptions());

		var probe = await backend.ProbeAsync(TestContext.Current.CancellationToken);

		Assert.False(probe.IsAvailable);
		Assert.Equal(InputErrorCode.BackendFault, probe.FailureCode);
		Assert.Contains("boom", probe.Reason);
	}
}

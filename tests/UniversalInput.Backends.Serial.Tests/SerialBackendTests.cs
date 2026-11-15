using UniversalInput.Backends.Serial.Options;
using UniversalInput.Backends.Serial.Tests.Fakes;
using UniversalInput.Core.Hub;
using UniversalInput.Testing.Harness;

namespace UniversalInput.Backends.Serial.Tests;

public class SerialBackendTests
{
	private static (InputHub Hub, FakeSerialPortSystem Ports) Create(params string[] ports)
	{
		var fake = new FakeSerialPortSystem();
		fake.SetPorts(ports);
		var backend = new SerialBackend(fake, new SerialBackendOptions { PollInterval = TimeSpan.FromMilliseconds(20) });
		return (TestInputHub.Create(backend), fake);
	}

	private static DeviceId IdOf(InputHub hub, string name) => hub.Devices.Single(d => d.Name == name).Id;

	[Fact]
	public async Task InitialPorts_BecomeSerialDevices_WithHonestOperations()
	{
		var (hub, _) = Create("COM3", "COM4");
		await using var owner = hub;

		await hub.StartAsync();

		Assert.Equal<string>(["COM3", "COM4"], hub.Devices.Select(d => d.Name));
		var device = hub.Devices[0];
		Assert.Equal(InputDeviceKind.Serial, device.Kind);
		Assert.Equal(DeviceBus.Serial, device.Bus);
		Assert.Equal(InputCapabilities.RawReports, device.Capabilities);
		Assert.Equal("COM3", device.Identity?.Path);
		Assert.Equal(SupportState.Supported, device.Operations[InputOperation.DirectAccess].State);
		Assert.Equal(SupportState.Unsupported, device.Operations[InputOperation.Observe].State);
		Assert.True(hub.GetSupport(InputOperation.DirectAccess, device.Id).IsSupported);
		Assert.False(hub.GetSupport(InputOperation.Observe, device.Id).IsSupported);

		var status = Assert.Single(hub.Backends);
		Assert.Equal(SerialBackend.BackendId, status.Id);
		Assert.Contains(InputOperation.DirectAccess, status.Roles);
		Assert.DoesNotContain(InputOperation.Emulate, status.Roles);
	}

	[Fact]
	public async Task HotPlug_IsDetectedByPolling()
	{
		var (hub, ports) = Create("COM3");
		await using var owner = hub;
		await using var subscription = hub.Subscribe(new SubscriptionOptions
		{
			Kinds = [InputEventKind.DeviceConnected, InputEventKind.DeviceDisconnected],
		});
		await hub.StartAsync();

		ports.SetPorts("COM3", "COM5");
		await Wait.UntilAsync(() => hub.Devices.Count == 2, description: "COM5 должен появиться");
		ports.SetPorts("COM5");
		await Wait.UntilAsync(() => hub.Devices.Count == 1, description: "COM3 должен исчезнуть");

		var events = await subscription.ReadEventsAsync(3);

		Assert.Equal<InputEventKind>(
			[InputEventKind.DeviceConnected, InputEventKind.DeviceConnected, InputEventKind.DeviceDisconnected],
			events.Select(e => e.Kind));
		Assert.Equal(events[0].Device, events[2].Device);
		Assert.Equal("COM5", Assert.Single(hub.Devices).Name);
	}

	[Fact]
	public async Task ARemovedPortThatReturns_KeepsItsDeviceId()
	{
		var (hub, ports) = Create("COM3");
		await using var owner = hub;
		await hub.StartAsync();
		var original = IdOf(hub, "COM3");

		ports.SetPorts();
		await Wait.UntilAsync(() => hub.Devices.Count == 0);
		ports.SetPorts("COM3");
		await Wait.UntilAsync(() => hub.Devices.Count == 1);

		Assert.Equal(original, IdOf(hub, "COM3"));
	}

	[Fact]
	public async Task OpenRaw_ReadsAndWritesBytes()
	{
		var (hub, ports) = Create("COM3");
		await using var owner = hub;
		await hub.StartAsync();

		await using var channel = await hub.OpenRawAsync(IdOf(hub, "COM3"), RawAccessMode.ReadWrite);
		var connection = ports.Connections["COM3"];
		connection.Push(1, 2, 3);
		var buffer = new byte[16];
		var length = await channel.ReadAsync(buffer, CancellationToken.None);
		byte[] request = [0x41, 0x54];
		await channel.WriteAsync(request, CancellationToken.None);

		byte[] expected = [1, 2, 3];
		Assert.Equal<byte>(expected, buffer.Take(length));
		Assert.Equal<byte>(request, Assert.Single(connection.Written));
	}

	[Fact]
	public async Task OpenRaw_WithoutReadMode_RejectsReading()
	{
		var (hub, _) = Create("COM3");
		await using var owner = hub;
		await hub.StartAsync();

		await using var channel = await hub.OpenRawAsync(IdOf(hub, "COM3"), RawAccessMode.Write);
		var error = await Assert.ThrowsAsync<InputException>(
			() => channel.ReadAsync(new byte[4], CancellationToken.None).AsTask());

		Assert.Equal(InputErrorCode.UnsupportedOperation, error.Code);
	}

	[Fact]
	public async Task UnpluggingAnOpenPort_FailsPendingReadWithDeviceUnavailable()
	{
		var (hub, ports) = Create("COM3");
		await using var owner = hub;
		await hub.StartAsync();
		var channel = await hub.OpenRawAsync(IdOf(hub, "COM3"), RawAccessMode.Read);

		var pending = channel.ReadAsync(new byte[8], CancellationToken.None).AsTask();
		ports.SetPorts();

		var error = await Assert.ThrowsAsync<InputException>(() => pending);
		Assert.Equal(InputErrorCode.DeviceUnavailable, error.Code);
		await Wait.UntilAsync(() => hub.Devices.Count == 0);
		Assert.True(ports.Connections["COM3"].IsDisposed);
	}

	[Fact]
	public async Task OpenRaw_WhenTheOsDeniesAccess_MapsTheErrorByPlatform()
	{
		var (hub, ports) = Create("COM3");
		await using var owner = hub;
		await hub.StartAsync();
		ports.OpenFailure = new UnauthorizedAccessException("Access denied");

		var error = await Assert.ThrowsAsync<InputException>(
			() => hub.OpenRawAsync(IdOf(hub, "COM3"), RawAccessMode.ReadWrite).AsTask());

		var expected = OperatingSystem.IsWindows() ? InputErrorCode.DeviceBusy : InputErrorCode.PermissionDenied;
		Assert.Equal(expected, error.Code);
		Assert.False(string.IsNullOrEmpty(error.Remediation));
		Assert.IsType<UnauthorizedAccessException>(error.InnerException);
	}

	[Fact]
	public async Task OpenRaw_AfterAFailedOpen_CanBeRetried()
	{
		var (hub, ports) = Create("COM3");
		await using var owner = hub;
		await hub.StartAsync();
		var device = IdOf(hub, "COM3");
		ports.OpenFailure = new IOException("порт исчез");

		var error = await Assert.ThrowsAsync<InputException>(
			() => hub.OpenRawAsync(device, RawAccessMode.Read).AsTask());
		ports.OpenFailure = null;
		await using var channel = await hub.OpenRawAsync(device, RawAccessMode.Read);

		Assert.Equal(InputErrorCode.DeviceUnavailable, error.Code);
		Assert.NotNull(channel);
	}

	[Fact]
	public async Task Probe_WhenPortsAreNotSupported_MarksTheBackendUnavailable()
	{
		var (hub, ports) = Create("COM3");
		await using var owner = hub;
		ports.FailListing(new PlatformNotSupportedException("нет поддержки"), times: int.MaxValue);

		await hub.StartAsync();

		var status = Assert.Single(hub.Backends);
		Assert.Equal(BackendState.Unavailable, status.State);
		Assert.Equal(InputErrorCode.BackendUnavailable, status.ErrorCode);
		Assert.Empty(hub.Devices);
	}

	[Fact]
	public async Task APollingFailure_RestartsTheBackend_AndKeepsDeviceIds()
	{
		var (hub, ports) = Create("COM3");
		await using var owner = hub;
		await hub.StartAsync();
		var original = IdOf(hub, "COM3");

		ports.FailListing(new IOException("sysfs недоступен"));

		await Wait.UntilAsync(
			() => hub.Backends[0].RestartCount >= 1 && hub.Backends[0].State == BackendState.Running,
			description: "backend должен перезапуститься после сбоя опроса");
		Assert.Equal(original, IdOf(hub, "COM3"));
	}

	[Fact]
	public async Task Stop_EndsPolling_AndRemovesDevices()
	{
		var (hub, ports) = Create("COM3");
		await using var owner = hub;
		await hub.StartAsync();

		await hub.StopAsync();
		var callsAfterStop = ports.ListCalls;
		await Task.Delay(150);

		Assert.Equal(callsAfterStop, ports.ListCalls);
		Assert.Empty(hub.Devices);
	}

	[Fact]
	public void InvalidOptions_AreRejected()
	{
		var fake = new FakeSerialPortSystem();

		Assert.Throws<ArgumentOutOfRangeException>(
			() => new SerialBackend(fake, new SerialBackendOptions { PollInterval = TimeSpan.Zero }));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new SerialBackend(fake, new SerialBackendOptions { DataBits = 9 }));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new SerialBackend(fake, new SerialBackendOptions { BaudRate = 0 }));
	}
}

using UniversalInput.Core.Backends;
using UniversalInput.Core.Hub;
using UniversalInput.Testing.Harness;
using UniversalInput.Testing.Mock;

namespace UniversalInput.Core.Tests.Hub;

public class InputHubLifecycleTests
{
	private static MockInputBackend WithKeyboard(string id = "mock")
	{
		var backend = new MockInputBackend(id);
		backend.InitialDevices.Add(MockInputBackend.Describe("kbd", InputDeviceKind.Keyboard));
		return backend;
	}

	private static BackendStatus StatusOf(InputHub hub, int index = 0) => hub.Backends[index];

	[Fact]
	public async Task Start_RunsTheBackend_AndEnumeratesInitialDevicesBeforeRunning()
	{
		var backend = WithKeyboard();
		await using var hub = TestInputHub.Create(backend);

		await hub.StartAsync();

		var status = Assert.Single(hub.Backends);
		Assert.Equal(BackendState.Running, status.State);
		Assert.Contains(InputOperation.Emulate, status.Roles);
		Assert.Contains(InputOperation.DirectAccess, status.Roles);
		Assert.Equal("kbd", Assert.Single(hub.Devices).Name);
		Assert.Equal(1, backend.StartCount);
	}

	[Fact]
	public async Task Start_Twice_Throws()
	{
		await using var hub = TestInputHub.Create(WithKeyboard());
		await hub.StartAsync();

		await Assert.ThrowsAsync<InvalidOperationException>(() => hub.StartAsync());
	}

	[Fact]
	public void DuplicateBackendIds_AreRejected()
	{
		Assert.Throws<ArgumentException>(() =>
			TestInputHub.Create(new MockInputBackend("same"), new MockInputBackend("same")));
	}

	[Fact]
	public async Task UnavailableBackend_IsReported_AndDoesNotStopOthers()
	{
		var bad = new MockInputBackend("bad")
		{
			ProbeResult = BackendProbe.Unavailable(InputErrorCode.PermissionDenied, "нет доступа", "выдайте доступ"),
		};
		var good = WithKeyboard("good");
		await using var hub = TestInputHub.Create(bad, good);

		await hub.StartAsync();

		var badStatus = StatusOf(hub, 0);
		Assert.Equal(BackendState.Unavailable, badStatus.State);
		Assert.Equal(InputErrorCode.PermissionDenied, badStatus.ErrorCode);
		Assert.Equal("выдайте доступ", badStatus.Remediation);
		Assert.Equal(0, bad.StartCount);
		Assert.Equal(BackendState.Running, StatusOf(hub, 1).State);
		Assert.Single(hub.Devices);
	}

	[Fact]
	public async Task ProbeException_BecomesUnavailableWithBackendFault()
	{
		var backend = new MockInputBackend { ProbeException = new DllNotFoundException("native.dll") };
		await using var hub = TestInputHub.Create(backend);

		await hub.StartAsync();

		var status = StatusOf(hub);
		Assert.Equal(BackendState.Unavailable, status.State);
		Assert.Equal(InputErrorCode.BackendFault, status.ErrorCode);
		Assert.Contains("native.dll", status.Reason);
	}

	[Fact]
	public async Task FailFast_ThrowsWhenARequiredBackendDoesNotStart()
	{
		var backend = new MockInputBackend
		{
			ProbeResult = BackendProbe.Unavailable(InputErrorCode.PlatformRestriction, "запрещено", "ничего нельзя сделать"),
		};
		var options = TestInputHub.FastOptions();
		options.FailFast = true;
		await using var hub = new InputHub([new BackendRegistration(backend, Required: true)], options);

		var error = await Assert.ThrowsAsync<InputException>(() => hub.StartAsync());

		Assert.Equal(InputErrorCode.PlatformRestriction, error.Code);
		Assert.Equal("ничего нельзя сделать", error.Remediation);
	}

	[Fact]
	public async Task WithoutFailFast_ARequiredBackendFailureIsOnlyReported()
	{
		var backend = new MockInputBackend
		{
			ProbeResult = BackendProbe.Unavailable(InputErrorCode.BackendUnavailable, "нет библиотеки"),
		};
		await using var hub = new InputHub([new BackendRegistration(backend, Required: true)], TestInputHub.FastOptions());

		await hub.StartAsync();

		Assert.Equal(BackendState.Unavailable, StatusOf(hub).State);
	}

	[Fact]
	public async Task RecoverableStartFailure_IsRetriedWithBackoff()
	{
		var backend = WithKeyboard();
		backend.StartFailuresRemaining = 2;
		await using var hub = TestInputHub.Create(backend);

		await hub.StartAsync();
		await Wait.UntilAsync(() => StatusOf(hub).State == BackendState.Running, description: "backend должен перезапуститься");

		Assert.Equal(3, backend.StartCount);
		Assert.Equal(2, StatusOf(hub).RestartCount);
		Assert.Single(hub.Devices);
	}

	[Fact]
	public async Task FatalStartFailure_IsNotRetried()
	{
		var backend = WithKeyboard();
		backend.StartFailuresRemaining = 5;
		backend.StartFailure = new InputException(InputErrorCode.PlatformRestriction, "запрещено ОС");
		await using var hub = TestInputHub.Create(backend);

		await hub.StartAsync();
		await Task.Delay(200);

		var status = StatusOf(hub);
		Assert.Equal(BackendState.Unavailable, status.State);
		Assert.Equal(InputErrorCode.PlatformRestriction, status.ErrorCode);
		Assert.Equal(1, backend.StartCount);
	}

	[Fact]
	public async Task RecoverableRuntimeFault_RestartsTheBackend_AndTheDeviceKeepsItsId()
	{
		var backend = WithKeyboard();
		await using var hub = TestInputHub.Create(backend);
		await using var subscription = hub.Subscribe();
		await hub.StartAsync();
		var originalId = backend.IdOf("kbd");

		backend.ReportFault(InputException.BackendFault("сбой потока опроса"), recoverable: true);

		await Wait.UntilAsync(
			() => backend.StartCount == 2 && StatusOf(hub).State == BackendState.Running,
			description: "backend должен перезапуститься после сбоя");

		Assert.Equal(originalId, Assert.Single(hub.Devices).Id);
		Assert.Equal(1, StatusOf(hub).RestartCount);

		var events = await subscription.ReadEventsAsync(3);
		Assert.Equal<InputEventKind>(
			[InputEventKind.DeviceConnected, InputEventKind.DeviceDisconnected, InputEventKind.DeviceConnected],
			events.Select(e => e.Kind));
		Assert.All(events, e => Assert.Equal(originalId, e.Device));
	}

	[Fact]
	public async Task UnrecoverableRuntimeFault_StopsTheBackend_AndRemovesItsDevices()
	{
		var backend = WithKeyboard();
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync();

		backend.ReportFault(InputException.BackendFault("необратимый сбой"), recoverable: false);

		await Wait.UntilAsync(() => StatusOf(hub).State == BackendState.Stopped, description: "backend должен остановиться");
		await Task.Delay(150);

		var status = StatusOf(hub);
		Assert.Equal(InputErrorCode.BackendFault, status.ErrorCode);
		Assert.Contains("необратимый сбой", status.Reason);
		Assert.Empty(hub.Devices);
		Assert.Equal(1, backend.StartCount);
	}

	[Fact]
	public async Task StopTimeout_ReleasesABackendThatDoesNotStop()
	{
		var backend = WithKeyboard();
		backend.HangOnStop = true;
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync();

		await hub.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Equal(BackendState.Stopped, StatusOf(hub).State);
		Assert.Equal(1, backend.StopCount);
		Assert.Empty(hub.Devices);
	}

	[Fact]
	public async Task Stop_DeliversDisconnects_ThenCompletesSubscriptions()
	{
		var backend = WithKeyboard();
		await using var hub = TestInputHub.Create(backend);
		var subscription = hub.Subscribe();
		await hub.StartAsync();

		await hub.StopAsync();

		List<InputEventKind> kinds = [];
		await foreach (var inputEvent in subscription.Reader.ReadAllAsync())
		{
			kinds.Add(inputEvent.Kind);
		}

		Assert.Equal<InputEventKind>([InputEventKind.DeviceConnected, InputEventKind.DeviceDisconnected], kinds);
	}

	[Fact]
	public async Task DisposeAsync_StopsAndDisposesBackendsExactlyOnce()
	{
		var backend = WithKeyboard();
		var hub = TestInputHub.Create(backend);
		await hub.StartAsync();

		await hub.DisposeAsync();
		await hub.DisposeAsync();

		Assert.Equal(1, backend.StopCount);
		Assert.Equal(1, backend.DisposeCount);
		Assert.Equal(BackendState.Disposed, StatusOf(hub).State);
		Assert.Throws<ObjectDisposedException>(() => hub.Subscribe());
	}

	[Fact]
	public void InvalidOptions_AreRejected()
	{
		var options = TestInputHub.FastOptions();
		options.BackendQueueCapacity = 1;

		Assert.Throws<ArgumentOutOfRangeException>(() => TestInputHub.Create(options, new MockInputBackend()));
	}
}

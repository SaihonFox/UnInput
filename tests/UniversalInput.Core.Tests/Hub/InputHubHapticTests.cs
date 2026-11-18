using UniversalInput.Testing.Harness;
using UniversalInput.Testing.Mock;

namespace UniversalInput.Core.Tests.Hub;

public class InputHubHapticTests
{
	private static readonly HapticEffect Rumble = new(0.5f, 1f, TimeSpan.FromMilliseconds(200));

	private static DeviceDescriptor Pad(string key, bool output) => new()
	{
		Key = new DeviceKey(key),
		Name = key,
		Kind = InputDeviceKind.Gamepad,
		Capabilities = InputCapabilities.GamepadButtons | InputCapabilities.Haptics,
		Operations = new Dictionary<InputOperation, OperationSupport>
		{
			[InputOperation.Observe] = OperationSupport.Supported,
			[InputOperation.Output] = output ? OperationSupport.Supported : OperationSupport.Unsupported("вибрации нет"),
		},
	};

	[Fact]
	public async Task PlayHaptic_ReachesTheBackendThatOwnsOutput()
	{
		var backend = new MockInputBackend { HapticsEnabled = true };
		backend.InitialDevices.Add(Pad("pad", output: true));
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var pad = backend.IdOf("pad");

		await hub.PlayHapticAsync(pad, Rumble, TestContext.Current.CancellationToken);

		Assert.True(hub.GetSupport(InputOperation.Output, pad).IsSupported);
		var played = Assert.Single(backend.Played);
		Assert.Equal(pad, played.Device);
		Assert.Equal(Rumble, played.Effect);
	}

	[Fact]
	public async Task PlayHaptic_OnADeviceWithoutOutput_IsRejectedWithTheDeclaredReason()
	{
		var backend = new MockInputBackend { HapticsEnabled = true };
		backend.InitialDevices.Add(Pad("pad", output: false));
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		var error = await Assert.ThrowsAsync<InputException>(
			() => hub.PlayHapticAsync(backend.IdOf("pad"), Rumble, TestContext.Current.CancellationToken).AsTask());

		Assert.Equal(InputErrorCode.UnsupportedOperation, error.Code);
		Assert.Contains("вибрации нет", error.Message);
		Assert.Empty(backend.Played);
	}

	[Fact]
	public async Task PlayHaptic_WhenNoBackendCanVibrate_IsUnsupported()
	{
		var backend = new MockInputBackend();
		backend.InitialDevices.Add(Pad("pad", output: true));
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		var error = await Assert.ThrowsAsync<InputException>(
			() => hub.PlayHapticAsync(backend.IdOf("pad"), Rumble, TestContext.Current.CancellationToken).AsTask());

		Assert.Equal(InputErrorCode.UnsupportedOperation, error.Code);
	}

	[Fact]
	public async Task PlayHaptic_OnADisconnectedDevice_IsRejected()
	{
		var backend = new MockInputBackend { HapticsEnabled = true };
		backend.InitialDevices.Add(Pad("pad", output: true));
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var pad = backend.IdOf("pad");
		backend.Disconnect(pad);
		await Wait.UntilAsync(() => hub.Devices.Count == 0);

		await Assert.ThrowsAsync<InputException>(
			() => hub.PlayHapticAsync(pad, Rumble, TestContext.Current.CancellationToken).AsTask());
	}

	[Fact]
	public async Task PlayHaptic_RequiresADevice()
	{
		await using var hub = TestInputHub.Create(new MockInputBackend { HapticsEnabled = true });
		await hub.StartAsync(TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<ArgumentException>(
			() => hub.PlayHapticAsync(default, Rumble, TestContext.Current.CancellationToken).AsTask());
	}
}

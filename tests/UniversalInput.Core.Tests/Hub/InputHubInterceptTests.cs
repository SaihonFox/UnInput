using UniversalInput.Testing.Harness;
using UniversalInput.Testing.Mock;

namespace UniversalInput.Core.Tests.Hub;

public class InputHubInterceptTests
{
	private static DeviceDescriptor Keyboard(bool intercept) => new()
	{
		Key = new DeviceKey("kbd"),
		Name = "kbd",
		Kind = InputDeviceKind.Keyboard,
		Capabilities = InputCapabilities.Keys,
		Operations = new Dictionary<InputOperation, OperationSupport>
		{
			[InputOperation.Observe] = OperationSupport.Supported,
			[InputOperation.Intercept] = intercept ? OperationSupport.Supported : OperationSupport.Unsupported("подавления нет"),
		},
	};

	[Fact]
	public async Task SetInterceptRule_ReachesTheBackend_WithOrWithoutADevice()
	{
		var backend = new MockInputBackend { InterceptEnabled = true };
		backend.InitialDevices.Add(Keyboard(intercept: true));
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync(TestContext.Current.CancellationToken);
		var first = new InterceptRule(keys: [KeyCodes.A]);
		var second = new InterceptRule(mouseButtons: [(int)MouseButton.Right]);

		hub.SetInterceptRule(first, backend.IdOf("kbd"));
		hub.SetInterceptRule(second);
		hub.SetInterceptRule(null);

		Assert.True(hub.GetSupport(InputOperation.Intercept, backend.IdOf("kbd")).IsSupported);
		Assert.Equal<InterceptRule?>([first, second, null], backend.InterceptRules);
	}

	[Fact]
	public async Task SetInterceptRule_WhenNoBackendCanSuppress_IsUnsupported()
	{
		var backend = new MockInputBackend();
		backend.InitialDevices.Add(Keyboard(intercept: true));
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		var error = Assert.Throws<InputException>(() => hub.SetInterceptRule(new InterceptRule(keys: [KeyCodes.A])));

		Assert.Equal(InputErrorCode.UnsupportedOperation, error.Code);
		Assert.Empty(backend.InterceptRules);
	}

	[Fact]
	public async Task SetInterceptRule_OnADeviceThatDeclaresNoInterception_IsRejectedWithTheDeclaredReason()
	{
		var backend = new MockInputBackend { InterceptEnabled = true };
		backend.InitialDevices.Add(Keyboard(intercept: false));
		await using var hub = TestInputHub.Create(backend);
		await hub.StartAsync(TestContext.Current.CancellationToken);

		var error = Assert.Throws<InputException>(
			() => hub.SetInterceptRule(new InterceptRule(keys: [KeyCodes.A]), backend.IdOf("kbd")));

		Assert.Equal(InputErrorCode.UnsupportedOperation, error.Code);
		Assert.Contains("подавления нет", error.Message);
		Assert.Empty(backend.InterceptRules);
	}
}

namespace UniversalInput.Abstractions.Devices;

/// <summary>Описание устройства, которое backend передаёт в <see cref="Backends.IBackendContext.AddDevice"/>.</summary>
public sealed record DeviceDescriptor
{
	public required DeviceKey Key { get; init; }
	public required string Name { get; init; }
	public required InputDeviceKind Kind { get; init; }
	public DeviceOrigin Origin { get; init; } = DeviceOrigin.Physical;
	public DeviceBus Bus { get; init; }
	public DeviceIdentity? Identity { get; init; }
	public InputCapabilities Capabilities { get; init; }

	/// <summary>Состояние операций, которые этот backend умеет выполнять с устройством.</summary>
	public IReadOnlyDictionary<InputOperation, OperationSupport> Operations { get; init; }
		= new Dictionary<InputOperation, OperationSupport>();

	/// <summary>Произвольные данные backend.</summary>
	public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
}

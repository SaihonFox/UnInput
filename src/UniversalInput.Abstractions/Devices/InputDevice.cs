namespace UniversalInput.Abstractions.Devices;

/// <summary>Устройство в реестре <see cref="Hub.IInputHub.Devices"/>.</summary>
public sealed record InputDevice
{
	public required DeviceId Id { get; init; }
	public required DeviceKey Key { get; init; }
	public required string Name { get; init; }
	public required InputDeviceKind Kind { get; init; }
	public required DeviceOrigin Origin { get; init; }

	/// <summary>Идентификатор backend, который обслуживает устройство.</summary>
	public required string BackendId { get; init; }
	public DeviceBus Bus { get; init; }
	public DeviceIdentity? Identity { get; init; }
	public InputCapabilities Capabilities { get; init; }
	public IReadOnlyDictionary<InputOperation, OperationSupport> Operations { get; init; }
		= new Dictionary<InputOperation, OperationSupport>();
	public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
}

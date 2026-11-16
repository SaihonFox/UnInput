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

	/// <summary>
	/// Backend, которые видят это устройство, в порядке убывания приоритета (первый — <see cref="BackendId"/>).
	/// Больше одного — когда Core объединил дескрипторы одного физического устройства.
	/// </summary>
	public IReadOnlyList<string> Providers { get; init; } = [];

	/// <summary>
	/// Какой backend исполняет каждую роль (<see cref="InputOperation.Observe"/>, <see cref="InputOperation.Emulate"/>,
	/// <see cref="InputOperation.DirectAccess"/>, <see cref="InputOperation.Output"/>) для этого устройства.
	/// Роли, которую не поддерживает ни один backend, в словаре нет.
	/// </summary>
	public IReadOnlyDictionary<InputOperation, string> RoleOwners { get; init; } = new Dictionary<InputOperation, string>();
}

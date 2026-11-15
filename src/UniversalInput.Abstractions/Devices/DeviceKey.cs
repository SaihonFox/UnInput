namespace UniversalInput.Abstractions.Devices;

/// <summary>Стабильный ключ устройства: переживает переподключение, по нему Core возвращает прежний <see cref="DeviceId"/>.</summary>
public readonly record struct DeviceKey(string Value)
{
	public override string ToString() => Value;
}

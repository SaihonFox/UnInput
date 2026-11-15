namespace UniversalInput.Abstractions.Devices;

/// <summary>Числовой идентификатор устройства. Выдаётся Core, действует в пределах процесса.</summary>
public readonly record struct DeviceId(int Value)
{
	/// <summary>Отсутствие устройства (например, у событий уровня backend).</summary>
	public static DeviceId None => default;

	public bool IsNone => Value == 0;

	public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

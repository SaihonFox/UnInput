namespace UniversalInput.Abstractions.Devices;

/// <summary>Шина подключения устройства.</summary>
public enum DeviceBus
{
	Unknown,
	Internal,
	Usb,
	Bluetooth,
	BluetoothLe,
	Serial,
	Network,
}

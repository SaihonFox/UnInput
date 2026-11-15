namespace UniversalInput.Abstractions.Devices;

/// <summary>Какие данные устройство производит или принимает.</summary>
[Flags]
public enum InputCapabilities : long
{
	None = 0,
	Keys = 1 << 0,

	/// <summary>Относительное движение указателя.</summary>
	PointerMotion = 1 << 1,

	/// <summary>Абсолютные координаты указателя.</summary>
	PointerPosition = 1 << 2,
	PointerButtons = 1 << 3,
	Wheel = 1 << 4,
	Touch = 1 << 5,
	GamepadButtons = 1 << 6,
	Axes = 1 << 7,
	Accelerometer = 1 << 8,
	Gyroscope = 1 << 9,
	Haptics = 1 << 10,
	RawReports = 1 << 11,
}

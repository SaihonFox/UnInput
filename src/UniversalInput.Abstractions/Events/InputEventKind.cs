namespace UniversalInput.Abstractions.Events;

/// <summary>Тип события. Значения от <see cref="Custom"/> зарезервированы под пользовательские типы backend.</summary>
public enum InputEventKind : ushort
{
	None = 0,

	KeyPressed,
	KeyReleased,

	MouseMoved,
	MouseButtonPressed,
	MouseButtonReleased,
	MouseWheel,

	GamepadButtonPressed,
	GamepadButtonReleased,
	GamepadAxisChanged,

	TouchStarted,
	TouchMoved,
	TouchEnded,

	SensorChanged,

	DeviceConnected,
	DeviceDisconnected,

	/// <summary>Потребитель пропустил события: сколько и почему — в <see cref="Views.StreamGapEvent"/>.</summary>
	StreamGap,

	/// <summary>Первый код пользовательских типов событий.</summary>
	Custom = 4096,
}

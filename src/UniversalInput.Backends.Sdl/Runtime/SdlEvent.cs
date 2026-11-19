namespace UniversalInput.Backends.Sdl.Runtime;

/// <summary>
/// Событие SDL в управляемом виде. <see cref="Code"/> — номер кнопки, оси или типа датчика в терминах SDL;
/// <see cref="Value"/> — сырое значение оси; <see cref="X"/>, <see cref="Y"/>, <see cref="Z"/> — показания датчика.
/// </summary>
internal readonly record struct SdlEvent(
	SdlEventKind Kind, uint Which, int Code = 0, short Value = 0, float X = 0, float Y = 0, float Z = 0)
{
	public static SdlEvent Added(uint which) => new(SdlEventKind.GamepadAdded, which);

	public static SdlEvent Removed(uint which) => new(SdlEventKind.GamepadRemoved, which);

	public static SdlEvent ButtonDown(uint which, int button) => new(SdlEventKind.ButtonDown, which, button);

	public static SdlEvent ButtonUp(uint which, int button) => new(SdlEventKind.ButtonUp, which, button);

	public static SdlEvent AxisMotion(uint which, int axis, short value) => new(SdlEventKind.Axis, which, axis, value);

	public static SdlEvent JoystickAdded(uint which) => new(SdlEventKind.JoystickAdded, which);

	public static SdlEvent JoystickRemoved(uint which) => new(SdlEventKind.JoystickRemoved, which);

	public static SdlEvent JoystickButtonDown(uint which, int button) => new(SdlEventKind.JoystickButtonDown, which, button);

	public static SdlEvent JoystickButtonUp(uint which, int button) => new(SdlEventKind.JoystickButtonUp, which, button);

	public static SdlEvent JoystickAxis(uint which, int axis, short value) => new(SdlEventKind.JoystickAxis, which, axis, value);

	public static SdlEvent JoystickHat(uint which, int hat, byte mask) => new(SdlEventKind.JoystickHat, which, hat, mask);

	public static SdlEvent SensorUpdate(uint which, int sensor, float x, float y, float z) =>
		new(SdlEventKind.Sensor, which, sensor, 0, x, y, z);
}

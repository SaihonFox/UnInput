namespace UniversalInput.Backends.Sdl.Runtime;

internal enum SdlEventKind
{
	/// <summary>Событие SDL, которое backend не использует.</summary>
	Other,
	GamepadAdded,
	GamepadRemoved,
	ButtonDown,
	ButtonUp,
	Axis,
	Sensor,

	/// <summary>События «сырых» джойстиков (без раскладки геймпада); <c>Code</c> — номер оси, кнопки или hat.</summary>
	JoystickAdded,
	JoystickRemoved,
	JoystickButtonDown,
	JoystickButtonUp,
	JoystickAxis,

	/// <summary><c>Value</c> — битовая маска SDL_HAT_* (вверх 1, вправо 2, вниз 4, влево 8).</summary>
	JoystickHat,
}

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
}

namespace UniversalInput.Abstractions.Devices;

/// <summary>
/// Вид устройства. Расширяемая структура: backend может создать свой вид
/// (<c>new InputDeviceKind("midi")</c>) без правок Core.
/// </summary>
public readonly record struct InputDeviceKind(string Name)
{
	public static InputDeviceKind Keyboard { get; } = new("keyboard");
	public static InputDeviceKind Mouse { get; } = new("mouse");
	public static InputDeviceKind Gamepad { get; } = new("gamepad");
	public static InputDeviceKind Joystick { get; } = new("joystick");
	public static InputDeviceKind Touch { get; } = new("touch");
	public static InputDeviceKind Hid { get; } = new("hid");
	public static InputDeviceKind Serial { get; } = new("serial");
	public static InputDeviceKind SystemWide { get; } = new("system");

	public override string ToString() => Name ?? string.Empty;
}

using System.Runtime.InteropServices;

namespace UniversalInput.Backends.Sdl.Runtime;

/// <summary>
/// Разбирает байты SDL_Event (SDL3). Раскладка полей взята из документации SDL3: у всех событий геймпада общий заголовок
/// из типа (4 Б), резерва (4 Б) и метки времени (8 Б), затем идентификатор экземпляра (4 Б) и поля конкретного события.
/// </summary>
internal static class SdlEventParser
{
	/// <summary>Размер SDL_Event в SDL3.</summary>
	public const int EventSize = 128;

	public const uint TypeAxisMotion = 0x650;
	public const uint TypeButtonDown = 0x651;
	public const uint TypeButtonUp = 0x652;
	public const uint TypeAdded = 0x653;
	public const uint TypeRemoved = 0x654;
	public const uint TypeSensorUpdate = 0x659;

	public const uint TypeJoystickAxisMotion = 0x600;
	public const uint TypeJoystickHatMotion = 0x602;
	public const uint TypeJoystickButtonDown = 0x603;
	public const uint TypeJoystickButtonUp = 0x604;
	public const uint TypeJoystickAdded = 0x605;
	public const uint TypeJoystickRemoved = 0x606;

	private const int WhichOffset = 16;

	public static SdlEvent Parse(ReadOnlySpan<byte> raw)
	{
		if (raw.Length < WhichOffset + 4)
		{
			return default;
		}

		var type = Read<uint>(raw, 0);
		var which = Read<uint>(raw, WhichOffset);

		switch (type)
		{
			case TypeAdded:
				return SdlEvent.Added(which);
			case TypeRemoved:
				return SdlEvent.Removed(which);

			case TypeButtonDown or TypeButtonUp when raw.Length >= 22:
				// Uint8 button в смещении 20; поле down (21) не нужно: направление задаёт тип события.
				return new SdlEvent(
					type == TypeButtonDown ? SdlEventKind.ButtonDown : SdlEventKind.ButtonUp, which, raw[20]);

			case TypeAxisMotion when raw.Length >= 26:
				// Uint8 axis в смещении 20, три байта выравнивания, Sint16 value в смещении 24.
				return SdlEvent.AxisMotion(which, raw[20], Read<short>(raw, 24));

			case TypeSensorUpdate when raw.Length >= 36:
				// Sint32 sensor в смещении 20, затем float data[3].
				return SdlEvent.SensorUpdate(
					which, Read<int>(raw, 20), Read<float>(raw, 24), Read<float>(raw, 28), Read<float>(raw, 32));

			case TypeJoystickAdded:
				return SdlEvent.JoystickAdded(which);
			case TypeJoystickRemoved:
				return SdlEvent.JoystickRemoved(which);

			case TypeJoystickButtonDown or TypeJoystickButtonUp when raw.Length >= 22:
				return new SdlEvent(
					type == TypeJoystickButtonDown ? SdlEventKind.JoystickButtonDown : SdlEventKind.JoystickButtonUp, which, raw[20]);

			case TypeJoystickAxisMotion when raw.Length >= 26:
				return SdlEvent.JoystickAxis(which, raw[20], Read<short>(raw, 24));

			case TypeJoystickHatMotion when raw.Length >= 22:
				// Uint8 hat в смещении 20, Uint8 value (маска SDL_HAT_*) в смещении 21.
				return SdlEvent.JoystickHat(which, raw[20], raw[21]);

			default:
				return default;
		}
	}

	private static T Read<T>(ReadOnlySpan<byte> raw, int offset)
		where T : struct => MemoryMarshal.Read<T>(raw[offset..]);
}

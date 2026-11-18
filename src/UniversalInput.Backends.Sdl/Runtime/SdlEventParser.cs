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

			default:
				return default;
		}
	}

	private static T Read<T>(ReadOnlySpan<byte> raw, int offset)
		where T : struct => MemoryMarshal.Read<T>(raw[offset..]);
}

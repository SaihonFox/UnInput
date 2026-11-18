namespace UniversalInput.Backends.Sdl.Mapping;

internal static class HapticMapper
{
	/// <summary>Нулевая или отрицательная длительность — остановка вибрации.</summary>
	public static (ushort Low, ushort High, uint Milliseconds) ToRumble(HapticEffect effect)
	{
		CheckIntensity(effect.LowFrequency, nameof(effect.LowFrequency));
		CheckIntensity(effect.HighFrequency, nameof(effect.HighFrequency));

		if (effect.Duration <= TimeSpan.Zero)
		{
			return (0, 0, 0);
		}

		var milliseconds = Math.Min(Math.Ceiling(effect.Duration.TotalMilliseconds), uint.MaxValue);
		return (ToUShort(effect.LowFrequency), ToUShort(effect.HighFrequency), (uint)milliseconds);
	}

	private static ushort ToUShort(float intensity) => (ushort)Math.Round(intensity * ushort.MaxValue);

	private static void CheckIntensity(float value, string name)
	{
		if (!(value >= 0f && value <= 1f))
		{
			throw new ArgumentOutOfRangeException(name, value, "Сила вибрации должна быть от 0 до 1.");
		}
	}
}

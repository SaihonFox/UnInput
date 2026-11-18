namespace UniversalInput.Backends.Sdl.Mapping;

/// <summary>
/// Перевод номеров SDL в типы библиотеки. Кнопки и оси SDL3 идут в том же порядке, что и <see cref="GamepadButton"/>
/// и <see cref="GamepadAxis"/>; порядок закреплён тестом. Дополнительные кнопки SDL (MISC, лепестки, тачпад) не передаются.
/// </summary>
internal static class GamepadMapper
{
	public const int SensorAccelerometer = 1;
	public const int SensorGyroscope = 2;

	private const int LastButton = (int)GamepadButton.DPadRight;
	private const int LastAxis = (int)GamepadAxis.RightTrigger;

	public static GamepadButton? Button(int sdlButton) => sdlButton is >= 0 and <= LastButton ? (GamepadButton)sdlButton : null;

	public static GamepadAxis? Axis(int sdlAxis) => sdlAxis is >= 0 and <= LastAxis ? (GamepadAxis)sdlAxis : null;

	/// <summary>
	/// Стики: от −1 до 1 (в SDL «вверх» — отрицательное значение, направление не меняется). Курки: от 0 до 1.
	/// </summary>
	public static float Normalize(GamepadAxis axis, short raw)
	{
		if (axis is GamepadAxis.LeftTrigger or GamepadAxis.RightTrigger)
		{
			return Math.Max(raw, (short)0) / 32767f;
		}

		return raw < 0 ? raw / 32768f : raw / 32767f;
	}

	/// <summary>Акселерометр — в м/с², гироскоп — в рад/с (единицы SDL). Датчики отдельных половин (левая/правая) не передаются.</summary>
	public static SensorKind? Sensor(int sdlSensor) => sdlSensor switch
	{
		SensorAccelerometer => SensorKind.Accelerometer,
		SensorGyroscope => SensorKind.Gyroscope,
		_ => null,
	};
}

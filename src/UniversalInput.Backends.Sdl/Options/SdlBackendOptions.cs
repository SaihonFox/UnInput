namespace UniversalInput.Backends.Sdl.Options;

public sealed class SdlBackendOptions
{
	/// <summary>
	/// Путь или имя нативной библиотеки SDL3. Проверяется первым; если не задан или не загрузился, пробуются
	/// стандартные имена для ОС (SDL3.dll, libSDL3.so.0, libSDL3.dylib). Саму библиотеку приложение поставляет само.
	/// </summary>
	public string? LibraryPath { get; set; }

	/// <summary>Включать ли акселерометр и гироскоп у геймпадов, которые их имеют. Датчики расходуют заряд и CPU, поэтому по умолчанию выключены.</summary>
	public bool EnableSensors { get; set; }

	/// <summary>
	/// Доставлять ли события, когда у приложения нет окна в фокусе (подсказка SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS).
	/// Для консольных программ и служб без окна нужно <c>true</c>; по умолчанию так и есть.
	/// </summary>
	public bool AllowBackgroundEvents { get; set; } = true;

	/// <summary>
	/// Показывать ли джойстики, которых нет в базе раскладок геймпадов SDL (штурвалы, педали, рули, ручки управления).
	/// Такое устройство получает <c>Kind = Joystick</c>, а события приходят с номерами «как у устройства»: номер кнопки в <c>Int0</c>
	/// события кнопки, номер оси в <c>Int0</c> события оси; каждый hat превращается в две оси после настоящих.
	/// По умолчанию выключено: часть служебных устройств ОС (например, на Linux) тоже выглядит как джойстик.
	/// </summary>
	public bool IncludeUnmappedJoysticks { get; set; }

	/// <summary>Сколько цикл ждёт событие до проверки остановки; задаёт и задержку остановки backend.</summary>
	public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(100);

	public void Validate()
	{
		if (PollInterval <= TimeSpan.Zero || PollInterval > TimeSpan.FromSeconds(5))
		{
			throw new ArgumentOutOfRangeException(nameof(PollInterval), PollInterval, "Интервал ожидания должен быть от 0 до 5 секунд.");
		}
	}
}

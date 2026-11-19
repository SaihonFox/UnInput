namespace UniversalInput.Backends.Sdl.Runtime;

/// <summary>
/// Работа с SDL. Инициализация, перечисление, открытие и ожидание событий вызываются из одного потока цикла backend;
/// вибрацию можно запускать из любого потока (SDL документирует <c>SDL_RumbleGamepad</c> как потокобезопасную).
/// Выделено, чтобы backend проверялся без нативной библиотеки.
/// </summary>
internal interface ISdlRuntime : IDisposable
{
	/// <summary>Загружает библиотеку и включает подсистему геймпадов; бросает <see cref="InputException"/> с причиной.</summary>
	void Initialize(bool allowBackgroundEvents);

	IReadOnlyList<uint> GetGamepadIds();

	/// <summary>Открывает геймпад; <c>null</c>, если SDL не смог (например, устройство уже исчезло).</summary>
	SdlGamepadInfo? OpenGamepad(uint instanceId, bool enableSensors);

	void CloseGamepad(uint instanceId);

	/// <summary>Идентификаторы джойстиков, у которых нет раскладки геймпада (геймпады из <see cref="GetGamepadIds"/> сюда не входят).</summary>
	IReadOnlyList<uint> GetRawJoystickIds();

	/// <summary>Открывает джойстик без раскладки геймпада; <c>null</c>, если он исчез или у него есть раскладка (его ведёт геймпадный путь).</summary>
	SdlJoystickInfo? OpenJoystick(uint instanceId);

	void CloseJoystick(uint instanceId);

	/// <summary>Ждёт событие не дольше <paramref name="timeout"/>; <c>false</c> — событий не было.</summary>
	bool TryWaitEvent(TimeSpan timeout, out SdlEvent sdlEvent);

	/// <summary>Запускает вибрацию. Интенсивность 0 и длительность 0 останавливают её. Бросает <see cref="InputException"/>, если устройство не вибрирует.</summary>
	void Rumble(uint instanceId, ushort lowFrequency, ushort highFrequency, uint durationMilliseconds);
}

namespace UniversalInput.Backends.GlobalHook.Emulation;

/// <summary>
/// Отправка событий ввода в ОС. Отделена от backend по той же причине, что и <c>IHookSource</c>: логику проверяют без настоящей ОС.
/// Клавиши — USB HID Usage, как во всём UniversalInput. Вызовы синхронные и короткие; вызывающий сам их сериализует.
/// </summary>
internal interface IHookEmulator : IDisposable
{
	EmulationStatus KeyDown(int usage);

	EmulationStatus KeyUp(int usage);

	/// <summary>Абсолютные координаты экрана или смещение — по <paramref name="absolute"/>.</summary>
	EmulationStatus MouseMove(float x, float y, bool absolute);

	/// <summary>Кнопка нажимается или отпускается там, где сейчас указатель: координаты события не используются, чтобы не «телепортировать» курсор.</summary>
	EmulationStatus SetMouseButton(MouseButton button, bool pressed);

	/// <summary>Y &gt; 0 — вверх, X &gt; 0 — вправо; единицы те же, что у наблюдаемых событий (по ОС различаются).</summary>
	EmulationStatus Wheel(float deltaX, float deltaY);
}

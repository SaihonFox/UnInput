namespace UniversalInput.Backends.GlobalHook.Sources;

/// <summary>
/// Событие хука в терминах UniversalInput: источник уже перевёл коды клавиш и кнопок, backend только строит <see cref="InputEvent"/>.
/// Для колеса <see cref="X"/> и <see cref="Y"/> — смещение (Y &gt; 0 — вверх, X &gt; 0 — вправо), иначе — координаты указателя.
/// </summary>
internal readonly record struct HookSignal
{
	public required HookSignalKind Kind { get; init; }

	/// <summary>USB HID Usage клавиши.</summary>
	public int Usage { get; init; }

	public KeyModifiers Modifiers { get; init; }

	public MouseButton Button { get; init; }

	public float X { get; init; }

	public float Y { get; init; }

	/// <summary>Событие создала программа (в том числе эмуляция), а не человек.</summary>
	public bool Simulated { get; init; }
}

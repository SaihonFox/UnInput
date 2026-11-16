using System.Diagnostics;

namespace UniversalInput.Abstractions.Events;

/// <summary>
/// Событие ввода: структура фиксированного размера без ссылок, чтобы высокочастотные события не создавали нагрузку на GC.
/// Поля <see cref="Int0"/>, <see cref="Int1"/> и <see cref="V0"/>..<see cref="V3"/> читаются типизированными
/// представлениями (<see cref="AsKey"/>, <see cref="AsPointer"/> и т. д.); пользовательские типы событий
/// (от <see cref="InputEventKind.Custom"/>) используют их напрямую.
/// </summary>
public readonly record struct InputEvent
{
	/// <summary>Монотонный номер; присваивает диспетчер Core, у backend он игнорируется.</summary>
	public long Sequence { get; init; }

	/// <summary>Метка времени в тиках <see cref="Stopwatch.GetTimestamp"/>.</summary>
	public long Timestamp { get; init; }

	public DeviceId Device { get; init; }

	/// <summary>Индекс backend; присваивает Core, расшифровка — через <see cref="Hub.IInputHub.Backends"/>.</summary>
	public ushort Backend { get; init; }

	public InputEventKind Kind { get; init; }
	public InputEventFlags Flags { get; init; }
	public KeyModifiers Modifiers { get; init; }

	public int Int0 { get; init; }
	public int Int1 { get; init; }
	public float V0 { get; init; }
	public float V1 { get; init; }
	public float V2 { get; init; }
	public float V3 { get; init; }

	public bool IsSimulated => (Flags & InputEventFlags.Simulated) != 0;

	/// <summary>
	/// Непрерывное событие: при переполнении канала его можно склеить с предыдущим.
	/// Остальные (клавиши, кнопки, начало и конец касания, подключение) не склеиваются и не отбрасываются молча.
	/// </summary>
	public bool IsContinuous => Kind is
		InputEventKind.MouseMoved or
		InputEventKind.MouseWheel or
		InputEventKind.GamepadAxisChanged or
		InputEventKind.TouchMoved or
		InputEventKind.SensorChanged;

	public InputEvent WithSequence(long sequence) => this with { Sequence = sequence };

	public InputEvent WithBackend(ushort backend) => this with { Backend = backend };

	public InputEvent WithDevice(DeviceId device) => this with { Device = device };

	public InputEvent WithFlags(InputEventFlags flags) => this with { Flags = flags };

	public KeyEvent AsKey() => new(Int0, Int1, Modifiers, (Flags & InputEventFlags.Repeat) != 0);

	public PointerEvent AsPointer() => new(V0, V1, (Flags & InputEventFlags.Absolute) != 0);

	public MouseButtonEvent AsMouseButton() => new(Int0, V0, V1);

	public WheelEvent AsWheel() => new(V0, V1);

	public GamepadButtonEvent AsGamepadButton() => new((GamepadButton)Int0);

	public AxisEvent AsAxis() => new(Int0, V0);

	public TouchEvent AsTouch() => new(Int0, V0, V1, V2);

	public SensorEvent AsSensor() => new((SensorKind)Int0, V0, V1, V2);

	public StreamGapEvent AsStreamGap() => new(Int0, (StreamGapReason)Int1);

	public override string ToString() => $"#{Sequence} {Kind} device={Device} backend={Backend}";

	// ---- Фабрики. timestamp = 0 означает «сейчас». ----

	private static long Now(long timestamp) => timestamp == 0 ? Stopwatch.GetTimestamp() : timestamp;

	public static InputEvent KeyDown(
		DeviceId device, int usage, int codepoint = 0, KeyModifiers modifiers = KeyModifiers.None,
		bool repeat = false, long timestamp = 0) => new()
		{
			Kind = InputEventKind.KeyPressed,
			Device = device,
			Timestamp = Now(timestamp),
			Int0 = usage,
			Int1 = codepoint,
			Modifiers = modifiers,
			Flags = repeat ? InputEventFlags.Repeat : InputEventFlags.None,
		};

	public static InputEvent KeyUp(
		DeviceId device, int usage, KeyModifiers modifiers = KeyModifiers.None, long timestamp = 0) => new()
		{
			Kind = InputEventKind.KeyReleased,
			Device = device,
			Timestamp = Now(timestamp),
			Int0 = usage,
			Modifiers = modifiers,
		};

	public static InputEvent MouseMove(
		DeviceId device, float x, float y, bool absolute = false, long timestamp = 0) => new()
		{
			Kind = InputEventKind.MouseMoved,
			Device = device,
			Timestamp = Now(timestamp),
			V0 = x,
			V1 = y,
			Flags = absolute ? InputEventFlags.Absolute : InputEventFlags.None,
		};

	public static InputEvent MouseDown(
		DeviceId device, int button, float x = 0, float y = 0, long timestamp = 0) => new()
		{
			Kind = InputEventKind.MouseButtonPressed,
			Device = device,
			Timestamp = Now(timestamp),
			Int0 = button,
			V0 = x,
			V1 = y,
		};

	public static InputEvent MouseUp(
		DeviceId device, int button, float x = 0, float y = 0, long timestamp = 0) => new()
		{
			Kind = InputEventKind.MouseButtonReleased,
			Device = device,
			Timestamp = Now(timestamp),
			Int0 = button,
			V0 = x,
			V1 = y,
		};

	/// <summary>Колесо: <paramref name="deltaY"/> &gt; 0 — прокрутка вверх, <paramref name="deltaX"/> &gt; 0 — вправо. Единицы задаёт backend и описывает в своей документации.</summary>
	public static InputEvent Wheel(DeviceId device, float deltaX, float deltaY, long timestamp = 0) => new()
	{
		Kind = InputEventKind.MouseWheel,
		Device = device,
		Timestamp = Now(timestamp),
		V0 = deltaX,
		V1 = deltaY,
	};

	public static InputEvent GamepadDown(DeviceId device, GamepadButton button, long timestamp = 0) => new()
	{
		Kind = InputEventKind.GamepadButtonPressed,
		Device = device,
		Timestamp = Now(timestamp),
		Int0 = (int)button,
	};

	public static InputEvent GamepadUp(DeviceId device, GamepadButton button, long timestamp = 0) => new()
	{
		Kind = InputEventKind.GamepadButtonReleased,
		Device = device,
		Timestamp = Now(timestamp),
		Int0 = (int)button,
	};

	public static InputEvent Axis(DeviceId device, int axis, float value, long timestamp = 0) => new()
	{
		Kind = InputEventKind.GamepadAxisChanged,
		Device = device,
		Timestamp = Now(timestamp),
		Int0 = axis,
		V0 = value,
	};

	public static InputEvent TouchStart(
		DeviceId device, int touchId, float x, float y, float pressure = 1, long timestamp = 0) => new()
		{
			Kind = InputEventKind.TouchStarted,
			Device = device,
			Timestamp = Now(timestamp),
			Int0 = touchId,
			V0 = x,
			V1 = y,
			V2 = pressure,
		};

	public static InputEvent TouchMove(
		DeviceId device, int touchId, float x, float y, float pressure = 1, long timestamp = 0) => new()
		{
			Kind = InputEventKind.TouchMoved,
			Device = device,
			Timestamp = Now(timestamp),
			Int0 = touchId,
			V0 = x,
			V1 = y,
			V2 = pressure,
		};

	public static InputEvent TouchEnd(DeviceId device, int touchId, float x, float y, long timestamp = 0) => new()
	{
		Kind = InputEventKind.TouchEnded,
		Device = device,
		Timestamp = Now(timestamp),
		Int0 = touchId,
		V0 = x,
		V1 = y,
	};

	public static InputEvent Sensor(
		DeviceId device, SensorKind sensor, float x, float y, float z, long timestamp = 0) => new()
		{
			Kind = InputEventKind.SensorChanged,
			Device = device,
			Timestamp = Now(timestamp),
			Int0 = (int)sensor,
			V0 = x,
			V1 = y,
			V2 = z,
		};

	public static InputEvent Connected(DeviceId device, long timestamp = 0) => new()
	{
		Kind = InputEventKind.DeviceConnected,
		Device = device,
		Timestamp = Now(timestamp),
	};

	public static InputEvent Disconnected(DeviceId device, long timestamp = 0) => new()
	{
		Kind = InputEventKind.DeviceDisconnected,
		Device = device,
		Timestamp = Now(timestamp),
	};

	public static InputEvent Gap(int lost, StreamGapReason reason, long timestamp = 0) => new()
	{
		Kind = InputEventKind.StreamGap,
		Timestamp = Now(timestamp),
		Int0 = lost,
		Int1 = (int)reason,
	};
}

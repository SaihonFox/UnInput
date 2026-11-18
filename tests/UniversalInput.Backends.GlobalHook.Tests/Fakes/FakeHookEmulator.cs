using UniversalInput.Backends.GlobalHook.Emulation;

namespace UniversalInput.Backends.GlobalHook.Tests.Fakes;

/// <summary>Эмулятор без ОС: записывает команды текстом, а результат подставляется тестом.</summary>
internal sealed class FakeHookEmulator : IHookEmulator
{
	public List<string> Calls { get; } = [];

	/// <summary>Что вернуть на следующие команды.</summary>
	public EmulationStatus Result { get; set; } = EmulationStatus.Success;

	public bool Disposed { get; private set; }

	public EmulationStatus KeyDown(int usage) => Record(FormattableString.Invariant($"down:{usage:X2}"));

	public EmulationStatus KeyUp(int usage) => Record(FormattableString.Invariant($"up:{usage:X2}"));

	public EmulationStatus MouseMove(float x, float y, bool absolute) =>
		Record(FormattableString.Invariant($"move:{(absolute ? "abs" : "rel")}:{x},{y}"));

	public EmulationStatus SetMouseButton(MouseButton button, bool pressed) =>
		Record($"button:{button}:{(pressed ? "down" : "up")}");

	public EmulationStatus Wheel(float deltaX, float deltaY) => Record(FormattableString.Invariant($"wheel:{deltaX},{deltaY}"));

	public void Dispose() => Disposed = true;

	private EmulationStatus Record(string call)
	{
		Calls.Add(call);
		return Result;
	}
}

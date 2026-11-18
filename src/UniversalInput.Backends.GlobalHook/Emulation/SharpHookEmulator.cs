using SharpHook;
using SharpHook.Data;
using UniversalInput.Backends.GlobalHook.Mapping;
using HookMouseButton = SharpHook.Data.MouseButton;
using InputMouseButton = UniversalInput.Abstractions.Events.MouseButton;

namespace UniversalInput.Backends.GlobalHook.Emulation;

/// <summary>
/// Эмуляция через симулятор SharpHook (libuiohook): SendInput на Windows, CGEvent на macOS, XTest или uinput на Linux.
/// Результат вызова библиотеки переводится в <see cref="EmulationStatus"/>; исключений здесь нет.
/// Координаты и вращение колеса у SharpHook целые 16-битные: значения округляются и обрезаются по этому диапазону.
/// </summary>
internal sealed class SharpHookEmulator : IHookEmulator
{
	private const string ApplicationName = "UniversalInput";

	private readonly IEventSimulator _simulator = EventSimulator.Create(ApplicationName);

	public EmulationStatus KeyDown(int usage) =>
		KeyMap.TryGetKeyCode(usage, out var code)
			? Translate(_simulator.SimulateKeyPress(code))
			: EmulationStatus.UnknownKey;

	public EmulationStatus KeyUp(int usage) =>
		KeyMap.TryGetKeyCode(usage, out var code)
			? Translate(_simulator.SimulateKeyRelease(code))
			: EmulationStatus.UnknownKey;

	public EmulationStatus MouseMove(float x, float y, bool absolute) => Translate(absolute
		? _simulator.SimulateMouseMovement(ToShort(x), ToShort(y))
		: _simulator.SimulateMouseMovementRelative(ToShort(x), ToShort(y)));

	public EmulationStatus SetMouseButton(InputMouseButton button, bool pressed)
	{
		if (!TryMapButton(button, out var target))
		{
			return EmulationStatus.Unsupported;
		}

		return Translate(pressed ? _simulator.SimulateMousePress(target) : _simulator.SimulateMouseRelease(target));
	}

	public EmulationStatus Wheel(float deltaX, float deltaY)
	{
		// У SharpHook положительное вращение — вверх или влево (см. SharpHookSource), у нас Y > 0 — вверх, X > 0 — вправо.
		var vertical = ToShort(deltaY);
		if (vertical != 0)
		{
			var status = Translate(_simulator.SimulateMouseWheel(vertical, MouseWheelScrollDirection.Vertical));
			if (status != EmulationStatus.Success)
			{
				return status;
			}
		}

		var horizontal = ToShort(-deltaX);
		return horizontal != 0
			? Translate(_simulator.SimulateMouseWheel(horizontal, MouseWheelScrollDirection.Horizontal))
			: EmulationStatus.Success;
	}

	public void Dispose() => (_simulator as IDisposable)?.Dispose();

	/// <summary>Результаты libuiohook, о которых известно что-то определённое; остальные — общий сбой.</summary>
	internal static EmulationStatus Translate(UioHookResult result) => result switch
	{
		UioHookResult.Success => EmulationStatus.Success,
		UioHookResult.ErrorUnsupportedFeature => EmulationStatus.Unsupported,
		UioHookResult.ErrorAxApiDisabled or UioHookResult.ErrorAxApiRevoked => EmulationStatus.AccessibilityDenied,
		UioHookResult.ErrorLinuxOpenUinput
			or UioHookResult.ErrorLinuxCreateUinputDevice
			or UioHookResult.ErrorLinuxWriteUinput
			or UioHookResult.ErrorLinuxVirtualDevicesNoInitialized => EmulationStatus.UinputUnavailable,
		UioHookResult.ErrorXOpenDisplay or UioHookResult.ErrorLinuxOpenWaylandDisplay => EmulationStatus.DisplayUnavailable,
		_ => EmulationStatus.Failed,
	};

	private static short ToShort(float value) =>
		(short)Math.Clamp(MathF.Round(value), short.MinValue, short.MaxValue);

	private static bool TryMapButton(InputMouseButton source, out HookMouseButton button)
	{
		switch (source)
		{
			case InputMouseButton.Left:
				button = HookMouseButton.Button1;
				return true;
			case InputMouseButton.Right:
				button = HookMouseButton.Button2;
				return true;
			case InputMouseButton.Middle:
				button = HookMouseButton.Button3;
				return true;
			case InputMouseButton.X1:
				button = HookMouseButton.Button4;
				return true;
			case InputMouseButton.X2:
				button = HookMouseButton.Button5;
				return true;
			default:
				button = default;
				return false;
		}
	}
}

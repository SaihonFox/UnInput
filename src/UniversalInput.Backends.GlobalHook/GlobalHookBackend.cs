using System.Diagnostics.CodeAnalysis;
using UniversalInput.Backends.GlobalHook.Emulation;
using UniversalInput.Backends.GlobalHook.Probe;
using UniversalInput.Backends.GlobalHook.Sources;

namespace UniversalInput.Backends.GlobalHook;

/// <summary>
/// Наблюдение за системным вводом (клавиатура и мышь) через глобальный хук ОС. Весь ввод системы — одно агрегатное
/// устройство вида <see cref="InputDeviceKind.SystemWide"/>: хук не говорит, какая именно физическая клавиатура или мышь
/// создала событие. Поэтому у событий стоит флаг <see cref="InputEventFlags.Aggregate"/>, а Core прячет их как дубли,
/// пока ту же категорию отдельно наблюдает backend устройств.
/// <para>
/// Умеет Observe, Emulate (клавиатура и мышь через симулятор SharpHook) и Intercept — подавление клавиш и кнопок мыши
/// по <see cref="InterceptRule"/>. Подавление в libuiohook есть только на Windows и macOS; на Linux оно честно объявлено
/// неподдерживаемым. Клавиши без кода на странице клавиатуры HID не передаются, не эмулируются и не подавляются (см. <c>KeyMap</c>).
/// <para>
/// Подавленное событие не доходит до других программ, но подписчикам хаба доставляется. События, созданные программой
/// (<see cref="InputEventFlags.Simulated"/>, в том числе собственной эмуляцией), не подавляются: иначе правило ломало бы эмуляцию.
/// Правило проверяется прямо на потоке хука и не должно его задерживать.
/// </para>
/// <para>
/// Эмуляция работает с агрегатным устройством (или без указания устройства). Модификаторы в событии не применяются:
/// чтобы нажать Shift+A, пошлите отдельные события для Shift и A. Автоповтор клавиши делает сама ОС, пока клавиша зажата.
/// Нажатие кнопки мыши происходит там, где сейчас указатель; координаты события для него игнорируются.
/// </para>
/// </para>
/// </summary>
public sealed class GlobalHookBackend : IInputBackend, IInputEmulator, IEventInterceptor
{
	public const string BackendId = "globalhook";

	private readonly Func<IHookSource> _createSource;
	private readonly Func<BackendProbe> _probe;
	private readonly Func<IHookEmulator> _createEmulator;
	private readonly bool _canSuppress;
	private InterceptRule? _rule;
	private readonly HashSet<int> _pressed = [];
	private readonly object _emulatorGate = new();
	private IHookEmulator? _emulator;
	private bool _active;
	private IHookSource? _source;
	private IBackendContext? _context;
	private DeviceId _device;

	public GlobalHookBackend()
		: this(() => new SharpHookSource(), GlobalHookProbe.ForCurrentSystem)
	{
	}

	/// <summary>
	/// Источник создаётся заново при каждом запуске: хук нельзя перезапустить после освобождения.
	/// Проверка платформы передаётся отдельно, чтобы тесты не зависели от ОС и графического сеанса, на которых идут.
	/// </summary>
	internal GlobalHookBackend(
		Func<IHookSource> createSource,
		Func<BackendProbe> probe,
		Func<IHookEmulator>? createEmulator = null,
		bool? canSuppress = null)
	{
		ArgumentNullException.ThrowIfNull(createSource);
		ArgumentNullException.ThrowIfNull(probe);
		_createSource = createSource;
		_probe = probe;
		_createEmulator = createEmulator ?? (() => new SharpHookEmulator());
		_canSuppress = canSuppress ?? (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
	}

	public string Id => BackendId;

	public ValueTask<BackendProbe> ProbeAsync(CancellationToken cancellationToken) => new(_probe());

	public async ValueTask StartAsync(IBackendContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		// Повторный запуск без остановки (перезапуск после сбоя): сначала освобождаем прежний хук.
		await ReleaseAsync().ConfigureAwait(false);

		var source = _createSource();
		_context = context;
		_device = context.AddDevice(Describe());
		lock (_pressed)
		{
			_pressed.Clear();
		}

		lock (_emulatorGate)
		{
			_active = true;
		}

		source.Signal += OnSignal;
		source.Failed += OnFailed;
		source.ShouldSuppress = ShouldSuppress;
		_source = source;

		try
		{
			await source.StartAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			await ReleaseAsync().ConfigureAwait(false);
			throw;
		}
		catch (Exception ex)
		{
			await ReleaseAsync().ConfigureAwait(false);
			throw HookFailure.ToException(ex);
		}
	}

	public async ValueTask StopAsync(CancellationToken cancellationToken) =>
		await ReleaseAsync().ConfigureAwait(false);

	public ValueTask DisposeAsync() => ReleaseAsync();

	public bool TryGetFeature<TFeature>([NotNullWhen(true)] out TFeature? feature)
		where TFeature : class, IBackendFeature
	{
		feature = (object)this as TFeature;
		if (typeof(TFeature) == typeof(IEventInterceptor) && !_canSuppress)
		{
			feature = null;
		}

		return feature is not null;
	}

	/// <summary>Правило действует и после перезапуска хука; <c>null</c> отключает подавление.</summary>
	public void SetRule(InterceptRule? rule)
	{
		if (!_canSuppress)
		{
			throw InputException.Unsupported("Подавление событий через глобальный хук работает только на Windows и macOS.");
		}

		Volatile.Write(ref _rule, rule);
	}

	private bool ShouldSuppress(HookSignal signal)
	{
		var rule = Volatile.Read(ref _rule);
		if (rule is null || signal.Simulated)
		{
			return false;
		}

		var candidate = signal.Kind switch
		{
			HookSignalKind.KeyDown => new InputEvent { Kind = InputEventKind.KeyPressed, Int0 = signal.Usage },
			HookSignalKind.KeyUp => new InputEvent { Kind = InputEventKind.KeyReleased, Int0 = signal.Usage },
			HookSignalKind.MouseDown => new InputEvent { Kind = InputEventKind.MouseButtonPressed, Int0 = (int)signal.Button },
			HookSignalKind.MouseUp => new InputEvent { Kind = InputEventKind.MouseButtonReleased, Int0 = (int)signal.Button },
			_ => default,
		};

		return rule.Matches(in candidate);
	}

	/// <summary>
	/// Симулятор создаётся при первой эмуляции и освобождается вместе с backend. Вызовы сериализуются:
	/// libuiohook не обещает безопасности одновременных вызовов симулятора из разных потоков.
	/// </summary>
	public ValueTask EmulateAsync(InputEvent inputEvent, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		lock (_emulatorGate)
		{
			if (!_active)
			{
				throw InputException.DeviceUnavailable("Backend глобального хука не запущен: эмулировать ввод нельзя.");
			}

			IHookEmulator emulator;
			try
			{
				emulator = _emulator ??= _createEmulator();
			}
			catch (Exception ex) when (ex is not InputException)
			{
				throw InputException.BackendUnavailable("Не удалось подготовить эмуляцию ввода: " + ex.Message, ex);
			}

			var (status, action) = Send(emulator, in inputEvent);
			if (status != EmulationStatus.Success)
			{
				throw EmulationFailure.ToException(status, action);
			}
		}

		return ValueTask.CompletedTask;
	}

	private static (EmulationStatus Status, string Action) Send(IHookEmulator emulator, in InputEvent e)
	{
		switch (e.Kind)
		{
			case InputEventKind.KeyPressed:
				return (emulator.KeyDown(e.Int0), $"нажатие клавиши 0x{e.Int0:X2}");

			case InputEventKind.KeyReleased:
				return (emulator.KeyUp(e.Int0), $"отпускание клавиши 0x{e.Int0:X2}");

			case InputEventKind.MouseMoved:
				var absolute = (e.Flags & InputEventFlags.Absolute) != 0;
				return (emulator.MouseMove(e.V0, e.V1, absolute), absolute ? "перемещение указателя" : "смещение указателя");

			case InputEventKind.MouseButtonPressed:
			case InputEventKind.MouseButtonReleased:
				var pressed = e.Kind == InputEventKind.MouseButtonPressed;
				var action = (pressed ? "нажатие" : "отпускание") + $" кнопки мыши {e.Int0}";
				return Enum.IsDefined(typeof(MouseButton), e.Int0)
					? (emulator.SetMouseButton((MouseButton)e.Int0, pressed), action)
					: (EmulationStatus.Unsupported, action);

			case InputEventKind.MouseWheel:
				return (emulator.Wheel(e.V0, e.V1), "прокрутка колеса");

			default:
				throw InputException.Unsupported($"Через глобальный хук можно эмулировать только клавиатуру и мышь, а не {e.Kind}.");
		}
	}

	private DeviceDescriptor Describe() => new()
	{
		Key = new DeviceKey("globalhook:system"),
		Name = "Системный ввод (глобальный хук)",
		Kind = InputDeviceKind.SystemWide,
		Origin = DeviceOrigin.Aggregate,
		Capabilities = InputCapabilities.Keys
			| InputCapabilities.PointerPosition
			| InputCapabilities.PointerButtons
			| InputCapabilities.Wheel,
		Operations = new Dictionary<InputOperation, OperationSupport>
		{
			[InputOperation.Enumerate] = OperationSupport.Supported,
			[InputOperation.Observe] = OperationSupport.Supported,
			[InputOperation.Emulate] = OperationSupport.Supported,
			[InputOperation.Intercept] = _canSuppress
				? OperationSupport.Supported
				: OperationSupport.Unsupported("Подавление событий в libuiohook работает только на Windows и macOS."),
			[InputOperation.DirectAccess] = OperationSupport.Unsupported("Хук не даёт доступа к сырым данным устройства."),
		},
		Properties = new Dictionary<string, string> { ["library"] = "SharpHook" },
	};

	/// <summary>Освобождает хук и убирает устройство. Идемпотентна: вызывается и при остановке, и после неудачного запуска.</summary>
	private async ValueTask ReleaseAsync()
	{
		lock (_emulatorGate)
		{
			_active = false;
			_emulator?.Dispose();
			_emulator = null;
		}

		var source = Interlocked.Exchange(ref _source, null);
		if (source is null)
		{
			return;
		}

		source.Signal -= OnSignal;
		source.Failed -= OnFailed;
		source.ShouldSuppress = null;

		var device = _device;
		_device = default;
		if (!device.IsNone)
		{
			_context?.RemoveDevice(device);
		}

		await source.DisposeAsync().ConfigureAwait(false);
	}

	private void OnFailed(Exception error)
	{
		if (Volatile.Read(ref _source) is null)
		{
			return;
		}

		_context?.ReportFault(InputException.BackendFault("Глобальный хук остановился: " + error.Message, error), recoverable: true);
	}

	private void OnSignal(HookSignal signal)
	{
		var context = _context;
		if (context is null || Volatile.Read(ref _source) is null)
		{
			return;
		}

		var device = _device;
		InputEvent inputEvent;
		switch (signal.Kind)
		{
			case HookSignalKind.KeyDown:
				bool repeat;
				lock (_pressed)
				{
					// Повторные KeyPressed без KeyReleased — автоповтор клавиши.
					repeat = !_pressed.Add(signal.Usage);
				}

				inputEvent = InputEvent.KeyDown(device, signal.Usage, repeat: repeat);
				break;

			case HookSignalKind.KeyUp:
				lock (_pressed)
				{
					_pressed.Remove(signal.Usage);
				}

				inputEvent = InputEvent.KeyUp(device, signal.Usage);
				break;

			case HookSignalKind.MouseMove:
				inputEvent = InputEvent.MouseMove(device, signal.X, signal.Y, absolute: true);
				break;

			case HookSignalKind.MouseDown:
				inputEvent = InputEvent.MouseDown(device, (int)signal.Button, signal.X, signal.Y);
				break;

			case HookSignalKind.MouseUp:
				inputEvent = InputEvent.MouseUp(device, (int)signal.Button, signal.X, signal.Y);
				break;

			case HookSignalKind.Wheel:
				inputEvent = InputEvent.Wheel(device, signal.X, signal.Y);
				break;

			default:
				return;
		}

		inputEvent = inputEvent with
		{
			Modifiers = signal.Modifiers,
			Flags = inputEvent.Flags | (signal.Simulated ? InputEventFlags.Simulated : InputEventFlags.None),
		};
		context.TryWrite(in inputEvent);
	}
}

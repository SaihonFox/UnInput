using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace UniversalInput.Testing.Mock;

/// <summary>
/// Управляемый backend для тестов библиотеки и приложений: устройства и события задаёт тест,
/// проверка, запуск и остановка могут «ломаться» по команде. Возможности эмуляции и прямого доступа включаются свойствами.
/// </summary>
public sealed class MockInputBackend : IInputBackend, IInputEmulator, IRawDeviceAccess, IHapticOutput, IEventInterceptor
{
	private readonly ConcurrentQueue<InputEvent> _emulated = new();
	private readonly ConcurrentDictionary<DeviceId, MockRawChannel> _channels = new();
	private readonly ConcurrentDictionary<string, DeviceId> _deviceIds = new();
	private IBackendContext? _context;

	public MockInputBackend(string id = "mock")
	{
		Id = id;
	}

	public string Id { get; }

	/// <summary>Результат <see cref="ProbeAsync"/>.</summary>
	public BackendProbe ProbeResult { get; set; } = BackendProbe.Available;

	/// <summary>Если задано, <see cref="ProbeAsync"/> бросает это исключение.</summary>
	public Exception? ProbeException { get; set; }

	/// <summary>Сколько ближайших вызовов <see cref="StartAsync"/> должны завершиться ошибкой <see cref="StartFailure"/>.</summary>
	public int StartFailuresRemaining { get; set; }

	/// <summary>Ошибка запуска; по умолчанию — восстановимый <see cref="InputErrorCode.BackendFault"/>.</summary>
	public Exception? StartFailure { get; set; }

	/// <summary>Если <c>true</c>, <see cref="StopAsync"/> не завершается, пока его не отменят (проверка тайм-аута остановки).</summary>
	public bool HangOnStop { get; set; }

	public bool EmulationEnabled { get; set; } = true;

	public bool RawAccessEnabled { get; set; } = true;

	/// <summary>Вибрация выключена по умолчанию: <see cref="Describe"/> её не объявляет, и роль Output у mock-backend не возникает сама.</summary>
	public bool HapticsEnabled { get; set; }

	/// <summary>Подавление событий; по умолчанию выключено, как у backend без хука с подавлением.</summary>
	public bool InterceptEnabled { get; set; }

	/// <summary>Устройства, которые появляются при запуске (первичное перечисление).</summary>
	public IList<DeviceDescriptor> InitialDevices { get; } = [];

	public int StartCount => Volatile.Read(ref _startCount);

	public int StopCount => Volatile.Read(ref _stopCount);

	public int DisposeCount => Volatile.Read(ref _disposeCount);

	/// <summary>События, пришедшие через <see cref="IInputEmulator"/>.</summary>
	public IReadOnlyCollection<InputEvent> Emulated => _emulated;

	/// <summary>Эффекты, пришедшие через <see cref="IHapticOutput"/>.</summary>
	public IReadOnlyCollection<(DeviceId Device, HapticEffect Effect)> Played => _played;

	/// <summary>Правила подавления в порядке установки; <c>null</c> — отключение.</summary>
	public IReadOnlyList<InterceptRule?> InterceptRules => [.. _interceptRules];

	public void SetRule(InterceptRule? rule) => _interceptRules.Enqueue(rule);

	private readonly ConcurrentQueue<(DeviceId Device, HapticEffect Effect)> _played = new();
	private readonly ConcurrentQueue<InterceptRule?> _interceptRules = new();
	private int _startCount;
	private int _stopCount;
	private int _disposeCount;

	/// <summary>Создаёт описание типового устройства для тестов.</summary>
	public static DeviceDescriptor Describe(
		string key,
		InputDeviceKind kind,
		InputCapabilities capabilities = InputCapabilities.Keys,
		DeviceOrigin origin = DeviceOrigin.Physical) => new()
		{
			Key = new DeviceKey(key),
			Name = key,
			Kind = kind,
			Origin = origin,
			Capabilities = capabilities,
			Operations = new Dictionary<InputOperation, OperationSupport>
			{
				[InputOperation.Enumerate] = OperationSupport.Supported,
				[InputOperation.Observe] = OperationSupport.Supported,
				[InputOperation.Emulate] = OperationSupport.Supported,
				[InputOperation.DirectAccess] = OperationSupport.Supported,
			},
		};

	/// <summary>Идентификатор устройства по ключу (после запуска).</summary>
	public DeviceId IdOf(string key) => _deviceIds[key];

	public DeviceId Connect(DeviceDescriptor descriptor)
	{
		var id = Context.AddDevice(descriptor);
		_deviceIds[descriptor.Key.Value] = id;
		return id;
	}

	public void Disconnect(DeviceId id) => Context.RemoveDevice(id);

	public bool Emit(in InputEvent inputEvent) => Context.TryWrite(in inputEvent);

	public void ReportFault(InputException error, bool recoverable) => Context.ReportFault(error, recoverable);

	/// <summary>Открытый для устройства канал прямого доступа (для проверок в тестах).</summary>
	public MockRawChannel? GetChannel(DeviceId id) => _channels.TryGetValue(id, out var channel) ? channel : null;

	public ValueTask<BackendProbe> ProbeAsync(CancellationToken cancellationToken)
	{
		if (ProbeException is not null)
		{
			throw ProbeException;
		}

		return new ValueTask<BackendProbe>(ProbeResult);
	}

	public ValueTask StartAsync(IBackendContext context, CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _startCount);

		if (StartFailuresRemaining > 0)
		{
			StartFailuresRemaining--;
			throw StartFailure ?? InputException.BackendFault("Имитация сбоя запуска.");
		}

		_context = context;
		foreach (var descriptor in InitialDevices)
		{
			_deviceIds[descriptor.Key.Value] = context.AddDevice(descriptor);
		}

		return ValueTask.CompletedTask;
	}

	public async ValueTask StopAsync(CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _stopCount);

		if (HangOnStop)
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
		}

		_context = null;
	}

	public bool TryGetFeature<TFeature>([NotNullWhen(true)] out TFeature? feature)
		where TFeature : class, IBackendFeature
	{
		feature = (object)this as TFeature;
		// Решает запрошенная возможность, а не тип объекта: сам backend реализует их все сразу.
		if ((typeof(TFeature) == typeof(IInputEmulator) && !EmulationEnabled)
			|| (typeof(TFeature) == typeof(IRawDeviceAccess) && !RawAccessEnabled)
			|| (typeof(TFeature) == typeof(IHapticOutput) && !HapticsEnabled)
			|| (typeof(TFeature) == typeof(IEventInterceptor) && !InterceptEnabled))
		{
			feature = null;
		}

		return feature is not null;
	}

	public ValueTask EmulateAsync(InputEvent inputEvent, CancellationToken cancellationToken)
	{
		_emulated.Enqueue(inputEvent);
		return ValueTask.CompletedTask;
	}

	public ValueTask PlayAsync(DeviceId id, HapticEffect effect, CancellationToken cancellationToken)
	{
		_played.Enqueue((id, effect));
		return ValueTask.CompletedTask;
	}

	public ValueTask<IRawDeviceChannel> OpenAsync(DeviceId id, RawAccessMode mode, CancellationToken cancellationToken)
	{
		var channel = new MockRawChannel(id, mode);
		_channels[id] = channel;
		return new ValueTask<IRawDeviceChannel>(channel);
	}

	public ValueTask DisposeAsync()
	{
		Interlocked.Increment(ref _disposeCount);
		return ValueTask.CompletedTask;
	}

	private IBackendContext Context =>
		_context ?? throw new InvalidOperationException("Backend не запущен: контекст недоступен.");
}

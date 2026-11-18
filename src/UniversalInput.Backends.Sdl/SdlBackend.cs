using System.Diagnostics.CodeAnalysis;
using UniversalInput.Backends.Sdl.Mapping;
using UniversalInput.Backends.Sdl.Native;
using UniversalInput.Backends.Sdl.Options;
using UniversalInput.Backends.Sdl.Runtime;

namespace UniversalInput.Backends.Sdl;

/// <summary>
/// Backend геймпадов через нативную библиотеку SDL3. Поддерживает наблюдение (кнопки, оси, при включении — акселерометр
/// и гироскоп) и вибрацию (<see cref="IHapticOutput"/>). Устройства без раскладки геймпада в базе SDL, а также
/// дополнительные кнопки (MISC, лепестки, тачпад) не показываются. Приложение, которое само читает события SDL,
/// не должно работать одновременно с этим backend: события получит тот, кто опросит очередь первым.
/// </summary>
public sealed class SdlBackend : IInputBackend, IHapticOutput
{
	public const string BackendId = "sdl";

	private readonly SdlBackendOptions _options;
	private readonly Func<ISdlRuntime> _createRuntime;
	private readonly Func<BackendProbe> _probe;
	private readonly object _gate = new();
	private readonly Dictionary<uint, DeviceId> _devices = new();
	private readonly Dictionary<DeviceId, uint> _instances = new();
	private ISdlRuntime? _runtime;
	private CancellationTokenSource? _stop;
	private Task _finished = Task.CompletedTask;
	private int _disposed;

	public SdlBackend(SdlBackendOptions? options = null)
		: this(null, null, options ?? new SdlBackendOptions())
	{
	}

	/// <summary>
	/// Среда выполнения и проверка передаются отдельно, чтобы тесты не зависели от библиотеки SDL3 на машине;
	/// без них используются настоящие.
	/// </summary>
	internal SdlBackend(Func<ISdlRuntime>? createRuntime, Func<BackendProbe>? probe, SdlBackendOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		options.Validate();

		_options = options;
		_createRuntime = createRuntime ?? (() => new SdlNativeRuntime(options.LibraryPath));
		_probe = probe ?? (() => SdlLibraryLocator.Probe(options.LibraryPath));
	}

	public string Id => BackendId;

	public ValueTask<BackendProbe> ProbeAsync(CancellationToken cancellationToken) => new(_probe());

	/// <summary>Запуск завершается, когда SDL включён и подключённые геймпады добавлены; цикл событий идёт в отдельном потоке.</summary>
	public async ValueTask StartAsync(IBackendContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		var runtime = _createRuntime();
		var stop = new CancellationTokenSource();
		var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var thread = new Thread(() => Run(runtime, context, ready, finished, stop.Token))
		{
			IsBackground = true,
			Name = "UniversalInput.Sdl",
		};

		lock (_gate)
		{
			_runtime = runtime;
			_stop = stop;
			_finished = finished.Task;
		}

		thread.Start();

		try
		{
			await ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			await stop.CancelAsync().ConfigureAwait(false);
			throw;
		}
	}

	public async ValueTask StopAsync(CancellationToken cancellationToken)
	{
		CancellationTokenSource? stop;
		Task finished;
		lock (_gate)
		{
			stop = _stop;
			finished = _finished;
			_stop = null;
		}

		if (stop is not null)
		{
			await stop.CancelAsync().ConfigureAwait(false);
		}

		try
		{
			await finished.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Core ограничивает остановку тайм-аутом; цикл завершится сам при ближайшем пробуждении.
		}

		stop?.Dispose();
	}

	public bool TryGetFeature<TFeature>([NotNullWhen(true)] out TFeature? feature)
		where TFeature : class, IBackendFeature
	{
		feature = (object)this as TFeature;
		return feature is not null;
	}

	public ValueTask PlayAsync(DeviceId id, HapticEffect effect, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var (low, high, milliseconds) = HapticMapper.ToRumble(effect);

		ISdlRuntime? runtime;
		uint instance;
		lock (_gate)
		{
			runtime = _runtime;
			if (!_instances.TryGetValue(id, out instance))
			{
				throw InputException.DeviceUnavailable($"Геймпад {id} не найден или отключён.");
			}
		}

		if (runtime is null)
		{
			throw InputException.DeviceUnavailable("Backend SDL остановлен.");
		}

		runtime.Rumble(instance, low, high, milliseconds);
		return ValueTask.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			await StopAsync(CancellationToken.None).ConfigureAwait(false);
		}
	}

	private void Run(
		ISdlRuntime runtime,
		IBackendContext context,
		TaskCompletionSource ready,
		TaskCompletionSource finished,
		CancellationToken stop)
	{
		try
		{
			runtime.Initialize(_options.AllowBackgroundEvents);

			foreach (var instance in runtime.GetGamepadIds())
			{
				AddGamepad(runtime, context, instance);
			}

			ready.TrySetResult();

			while (!stop.IsCancellationRequested)
			{
				if (runtime.TryWaitEvent(_options.PollInterval, out var sdlEvent))
				{
					Handle(runtime, context, sdlEvent);
				}
			}
		}
		catch (Exception ex)
		{
			var failure = SdlFailure.ToException(ex);
			if (!ready.TrySetException(failure))
			{
				context.ReportFault(failure, recoverable: true);
			}
		}
		finally
		{
			lock (_gate)
			{
				_runtime = null;
				_devices.Clear();
				_instances.Clear();
			}

			try
			{
				runtime.Dispose();
			}
			finally
			{
				finished.TrySetResult();
			}
		}
	}

	private void Handle(ISdlRuntime runtime, IBackendContext context, in SdlEvent sdlEvent)
	{
		switch (sdlEvent.Kind)
		{
			case SdlEventKind.GamepadAdded:
				AddGamepad(runtime, context, sdlEvent.Which);
				break;

			case SdlEventKind.GamepadRemoved:
				RemoveGamepad(runtime, context, sdlEvent.Which);
				break;

			case SdlEventKind.ButtonDown or SdlEventKind.ButtonUp:
				if (Find(sdlEvent.Which) is { } padForButton && GamepadMapper.Button(sdlEvent.Code) is { } button)
				{
					Write(context, sdlEvent.Kind == SdlEventKind.ButtonDown
						? InputEvent.GamepadDown(padForButton, button)
						: InputEvent.GamepadUp(padForButton, button));
				}

				break;

			case SdlEventKind.Axis:
				if (Find(sdlEvent.Which) is { } padForAxis && GamepadMapper.Axis(sdlEvent.Code) is { } axis)
				{
					Write(context, InputEvent.Axis(padForAxis, (int)axis, GamepadMapper.Normalize(axis, sdlEvent.Value)));
				}

				break;

			case SdlEventKind.Sensor:
				if (Find(sdlEvent.Which) is { } padForSensor && GamepadMapper.Sensor(sdlEvent.Code) is { } sensor)
				{
					Write(context, InputEvent.Sensor(padForSensor, sensor, sdlEvent.X, sdlEvent.Y, sdlEvent.Z));
				}

				break;
		}
	}

	private static void Write(IBackendContext context, in InputEvent inputEvent) => context.TryWrite(in inputEvent);

	private DeviceId? Find(uint instance)
	{
		lock (_gate)
		{
			return _devices.TryGetValue(instance, out var id) ? id : null;
		}
	}

	private void AddGamepad(ISdlRuntime runtime, IBackendContext context, uint instance)
	{
		lock (_gate)
		{
			if (_devices.ContainsKey(instance))
			{
				return;
			}
		}

		// SDL присылает «добавлен» и для уже подключённых устройств: повтор отсекается выше, а тут — исчезнувшее до открытия.
		if (runtime.OpenGamepad(instance, _options.EnableSensors) is not { } info)
		{
			return;
		}

		var id = context.AddDevice(Describe(info));
		lock (_gate)
		{
			_devices[instance] = id;
			_instances[id] = instance;
		}
	}

	private void RemoveGamepad(ISdlRuntime runtime, IBackendContext context, uint instance)
	{
		DeviceId id;
		lock (_gate)
		{
			if (!_devices.Remove(instance, out id))
			{
				return;
			}

			_instances.Remove(id);
		}

		runtime.CloseGamepad(instance);
		context.RemoveDevice(id);
	}

	/// <summary>
	/// Ключ устройства: путь, если SDL его знает (он постоянен для порта), иначе VID, PID и серийный номер.
	/// Номер экземпляра SDL при переподключении меняется, поэтому в ключ он попадает только как крайняя мера для одинаковых устройств без серийного номера.
	/// </summary>
	internal static string KeyOf(SdlGamepadInfo info) =>
		!string.IsNullOrEmpty(info.Path)
			? "sdl:" + info.Path
			: $"sdl:{info.VendorId:X4}:{info.ProductId:X4}:{(string.IsNullOrEmpty(info.Serial) ? "id" + info.InstanceId : info.Serial)}";

	private static DeviceDescriptor Describe(SdlGamepadInfo info)
	{
		var capabilities = InputCapabilities.GamepadButtons | InputCapabilities.Axes;
		if (info.HasRumble)
		{
			capabilities |= InputCapabilities.Haptics;
		}

		if (info.HasAccelerometer)
		{
			capabilities |= InputCapabilities.Accelerometer;
		}

		if (info.HasGyroscope)
		{
			capabilities |= InputCapabilities.Gyroscope;
		}

		Dictionary<string, string> properties = new() { ["library"] = "SDL3" };
		if (!string.IsNullOrEmpty(info.Path))
		{
			properties["path"] = info.Path;
		}

		return new DeviceDescriptor
		{
			Key = new DeviceKey(KeyOf(info)),
			Name = info.Name,
			Kind = InputDeviceKind.Gamepad,
			Origin = DeviceOrigin.Physical,
			Bus = DeviceBus.Unknown,
			Identity = new DeviceIdentity(
				ToUShort(info.VendorId),
				ToUShort(info.ProductId),
				string.IsNullOrEmpty(info.Serial) ? null : info.Serial,
				info.Path),
			Capabilities = capabilities,
			Operations = new Dictionary<InputOperation, OperationSupport>
			{
				[InputOperation.Enumerate] = OperationSupport.Supported,
				[InputOperation.Observe] = OperationSupport.Supported,

				// SDL не сообщает заранее, вибрирует ли конкретная модель; если нет, PlayHapticAsync вернёт Unsupported с причиной SDL.
				[InputOperation.Output] = OperationSupport.Supported,
				[InputOperation.Emulate] = OperationSupport.Unsupported("Backend не создаёт виртуальные геймпады."),
				[InputOperation.DirectAccess] = OperationSupport.Unsupported("SDL не даёт сырого доступа к отчётам; для этого есть backend Hid."),
			},
			Properties = properties,
		};
	}

	private static ushort? ToUShort(int value) => value is >= 0 and <= ushort.MaxValue ? (ushort)value : null;
}

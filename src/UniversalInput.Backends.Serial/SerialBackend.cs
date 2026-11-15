using System.Diagnostics.CodeAnalysis;
using UniversalInput.Backends.Serial.Channels;
using UniversalInput.Backends.Serial.Options;
using UniversalInput.Backends.Serial.Ports;

namespace UniversalInput.Backends.Serial;

/// <summary>
/// Backend COM-портов. Каждый порт — устройство вида <see cref="InputDeviceKind.Serial"/>; данные отдаются
/// сырыми байтами через <see cref="IRawDeviceAccess"/>, а не событиями ввода (операция Observe честно объявлена неподдерживаемой).
/// Подключение и отключение обнаруживаются опросом списка портов.
/// </summary>
public sealed class SerialBackend : IInputBackend, IRawDeviceAccess
{
	public const string BackendId = "serial";

	private readonly SerialBackendOptions _options;
	private readonly ISerialPortSystem _system;
	private readonly object _gate = new();
	private readonly Dictionary<string, DeviceId> _ids = new(StringComparer.Ordinal);
	private readonly Dictionary<DeviceId, string> _names = new();
	private CancellationTokenSource? _cts;
	private Task _poller = Task.CompletedTask;
	private int _stopped;

	public SerialBackend(SerialBackendOptions? options = null)
		: this(new SystemSerialPorts(), options ?? new SerialBackendOptions())
	{
	}

	internal SerialBackend(ISerialPortSystem system, SerialBackendOptions options)
	{
		ArgumentNullException.ThrowIfNull(system);
		ArgumentNullException.ThrowIfNull(options);
		options.Validate();

		_system = system;
		_options = options;
	}

	public string Id => BackendId;

	public ValueTask<BackendProbe> ProbeAsync(CancellationToken cancellationToken)
	{
		if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsBrowser())
		{
			return new ValueTask<BackendProbe>(BackendProbe.Unavailable(
				InputErrorCode.PlatformRestriction,
				"Последовательные порты через System.IO.Ports на этой платформе не поддерживаются."));
		}

		try
		{
			_system.GetPortNames();
		}
		catch (PlatformNotSupportedException ex)
		{
			return new ValueTask<BackendProbe>(BackendProbe.Unavailable(InputErrorCode.BackendUnavailable, ex.Message));
		}
		catch (Exception ex)
		{
			return new ValueTask<BackendProbe>(BackendProbe.Unavailable(
				InputErrorCode.BackendFault, "Не удалось получить список портов: " + ex.Message));
		}

		return new ValueTask<BackendProbe>(BackendProbe.Available);
	}

	/// <summary>Первичное перечисление выполняется до возврата: к моменту Running список устройств согласован.</summary>
	public ValueTask StartAsync(IBackendContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		Interlocked.Exchange(ref _stopped, 0);
		Refresh(context);

		var cts = new CancellationTokenSource();
		_cts = cts;
		_poller = Task.Run(() => PollAsync(context, cts.Token), CancellationToken.None);
		return ValueTask.CompletedTask;
	}

	public async ValueTask StopAsync(CancellationToken cancellationToken)
	{
		var cts = _cts;
		_cts = null;

		if (cts is not null)
		{
			await cts.CancelAsync().ConfigureAwait(false);
		}

		try
		{
			await _poller.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Core ограничивает остановку тайм-аутом; опрос и так прекращён.
		}

		cts?.Dispose();

		lock (_gate)
		{
			_ids.Clear();
			_names.Clear();
		}
	}

	public bool TryGetFeature<TFeature>([NotNullWhen(true)] out TFeature? feature)
		where TFeature : class, IBackendFeature
	{
		feature = (object)this as TFeature;
		return feature is not null;
	}

	public async ValueTask<IRawDeviceChannel> OpenAsync(
		DeviceId id, RawAccessMode mode, CancellationToken cancellationToken)
	{
		string? name;
		lock (_gate)
		{
			_names.TryGetValue(id, out name);
		}

		if (name is null)
		{
			throw InputException.DeviceUnavailable($"Порт для устройства {id} не найден или отключён.");
		}

		try
		{
			var connection = await _system.OpenAsync(name, _options, cancellationToken).ConfigureAwait(false);
			return new SerialChannel(connection, mode, name);
		}
		catch (UnauthorizedAccessException ex)
		{
			throw MapAccessDenied(name, ex);
		}
		catch (Exception ex) when (ex is IOException or FileNotFoundException or DirectoryNotFoundException)
		{
			throw InputException.DeviceUnavailable($"Порт {name} недоступен: {ex.Message}", ex);
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _stopped, 1) == 0)
		{
			await StopAsync(CancellationToken.None).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// <see cref="UnauthorizedAccessException"/> бывает и «порт занят» (Windows), и «нет прав» (Linux, macOS),
	/// поэтому код ошибки зависит от ОС.
	/// </summary>
	private static InputException MapAccessDenied(string portName, UnauthorizedAccessException exception)
	{
		if (OperatingSystem.IsWindows())
		{
			return new InputException(
				InputErrorCode.DeviceBusy,
				$"Порт {portName} занят другим приложением или недоступен.",
				exception)
			{
				Remediation = "Закройте программу, которая использует порт.",
			};
		}

		return new InputException(
			InputErrorCode.PermissionDenied,
			$"Нет прав на открытие порта {portName}.",
			exception)
		{
			Remediation = "Добавьте пользователя в группу dialout (Linux) или выдайте доступ к устройству (macOS).",
		};
	}

	private async Task PollAsync(IBackendContext context, CancellationToken cancellationToken)
	{
		try
		{
			using var timer = new PeriodicTimer(_options.PollInterval);
			while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
			{
				Refresh(context);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			context.ReportFault(
				InputException.BackendFault("Опрос последовательных портов завершился ошибкой.", ex),
				recoverable: true);
		}
	}

	/// <summary>Сверяет список портов ОС с известными: сначала убирает исчезнувшие, затем добавляет новые.</summary>
	private void Refresh(IBackendContext context)
	{
		var current = _system.GetPortNames().ToHashSet(StringComparer.Ordinal);

		List<DeviceId> removed = [];
		lock (_gate)
		{
			foreach (var name in _ids.Keys.Where(n => !current.Contains(n)).ToList())
			{
				var id = _ids[name];
				_ids.Remove(name);
				_names.Remove(id);
				removed.Add(id);
			}
		}

		foreach (var id in removed)
		{
			context.RemoveDevice(id);
		}

		foreach (var name in current.OrderBy(n => n, StringComparer.Ordinal))
		{
			bool known;
			lock (_gate)
			{
				known = _ids.ContainsKey(name);
			}

			if (known)
			{
				continue;
			}

			var id = context.AddDevice(Describe(name));
			lock (_gate)
			{
				_ids[name] = id;
				_names[id] = name;
			}
		}
	}

	private static DeviceDescriptor Describe(string portName) => new()
	{
		Key = new DeviceKey("serial:" + portName),
		Name = portName,
		Kind = InputDeviceKind.Serial,
		Origin = DeviceOrigin.Physical,
		Bus = DeviceBus.Serial,
		Identity = new DeviceIdentity(Path: portName),
		Capabilities = InputCapabilities.RawReports,
		Operations = new Dictionary<InputOperation, OperationSupport>
		{
			[InputOperation.Enumerate] = OperationSupport.Supported,
			[InputOperation.DirectAccess] = OperationSupport.Supported,
			[InputOperation.Observe] = new(
				SupportState.Unsupported,
				"Последовательный порт отдаёт сырые байты через прямой доступ, а не события ввода."),
			[InputOperation.Emulate] = new(
				SupportState.Unsupported,
				"Эмуляция ввода через последовательный порт не поддерживается."),
		},
	};
}

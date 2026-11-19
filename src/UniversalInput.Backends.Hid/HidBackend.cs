using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using UniversalInput.Backends.Hid.Channels;
using UniversalInput.Backends.Hid.Decoding;
using UniversalInput.Backends.Hid.Discovery;
using UniversalInput.Backends.Hid.Observation;
using UniversalInput.Backends.Hid.Options;
using UniversalInput.Backends.Hid.Platform;

namespace UniversalInput.Backends.Hid;

/// <summary>
/// Backend HID-устройств (HidSharp). Каждый HID-интерфейс — отдельное устройство с VID, PID и серийным номером,
/// по которым Core сопоставляет его с тем же прибором из других backend. Данные отдаются сырыми отчётами через
/// <see cref="IRawDeviceAccess"/>. Если включить <see cref="HidBackendOptions.ObserveInputDevices"/>, отчёты клавиатур и мышей
/// ещё и разбираются в события ввода (Observe); иначе, а также для устройств другого класса или тех, которые не удалось
/// открыть, Observe честно объявлен неподдерживаемым с причиной. Шина подключения HidSharp не сообщает (<see cref="DeviceBus.Unknown"/>), и сопоставление
/// с устройствами, у которых шина известна, из-за этого не происходит.
/// </summary>
public sealed class HidBackend : IInputBackend, IRawDeviceAccess
{
	public const string BackendId = "hid";

	private readonly HidBackendOptions _options;
	private readonly IHidPlatform _platform;
	private readonly object _gate = new();
	private readonly Dictionary<string, DeviceId> _ids = new(StringComparer.Ordinal);
	private readonly Dictionary<DeviceId, HidDeviceInfo> _infos = new();
	private readonly Dictionary<DeviceId, HidObserver> _observers = new();
	private CancellationTokenSource? _cts;
	private Task _loop = Task.CompletedTask;
	private Timer? _timer;
	private IDisposable? _subscription;
	private int _stopped;

	public HidBackend(HidBackendOptions? options = null)
		: this(new HidSharpPlatform(), options ?? new HidBackendOptions())
	{
	}

	internal HidBackend(IHidPlatform platform, HidBackendOptions options)
	{
		ArgumentNullException.ThrowIfNull(platform);
		ArgumentNullException.ThrowIfNull(options);
		options.Validate();

		_platform = platform;
		_options = options;
	}

	public string Id => BackendId;

	public ValueTask<BackendProbe> ProbeAsync(CancellationToken cancellationToken)
	{
		if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsBrowser() || OperatingSystem.IsTvOS())
		{
			return new ValueTask<BackendProbe>(BackendProbe.Unavailable(
				InputErrorCode.PlatformRestriction,
				"HidSharp работает на Windows, Linux и macOS; на мобильных платформах и в браузере доступа к HID-устройствам нет."));
		}

		try
		{
			_platform.GetDevices();
		}
		catch (Exception ex)
		{
			var failure = HidFailure.ToException(ex);
			return new ValueTask<BackendProbe>(BackendProbe.Unavailable(failure.Code, failure.Message, failure.Remediation));
		}

		return new ValueTask<BackendProbe>(BackendProbe.Available);
	}

	/// <summary>Первичное перечисление выполняется до возврата: к моменту Running список устройств согласован.</summary>
	public ValueTask StartAsync(IBackendContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		Interlocked.Exchange(ref _stopped, 0);
		try
		{
			Refresh(context);
		}
		catch (Exception ex)
		{
			throw HidFailure.ToException(ex);
		}

		var signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
		{
			FullMode = BoundedChannelFullMode.DropWrite,
			SingleReader = true,
		});
		var cts = new CancellationTokenSource();
		_cts = cts;
		_subscription = _platform.SubscribeChanged(() => signal.Writer.TryWrite(true));
		_timer = new Timer(_ => signal.Writer.TryWrite(true), null, _options.RescanInterval, _options.RescanInterval);
		_loop = Task.Run(() => RescanAsync(context, signal.Reader, cts.Token), CancellationToken.None);
		return ValueTask.CompletedTask;
	}

	public async ValueTask StopAsync(CancellationToken cancellationToken)
	{
		var cts = _cts;
		_cts = null;

		_subscription?.Dispose();
		_subscription = null;
		if (_timer is { } timer)
		{
			await timer.DisposeAsync().ConfigureAwait(false);
			_timer = null;
		}

		if (cts is not null)
		{
			await cts.CancelAsync().ConfigureAwait(false);
		}

		try
		{
			await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Core ограничивает остановку тайм-аутом; пересчёт и так прекращён.
		}

		cts?.Dispose();

		List<HidObserver> observers;
		lock (_gate)
		{
			_ids.Clear();
			_infos.Clear();
			observers = [.. _observers.Values];
			_observers.Clear();
		}

		await Task.WhenAll(observers.Select(observer => observer.StopAsync())).ConfigureAwait(false);
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
		HidDeviceInfo? info;
		bool observed;
		lock (_gate)
		{
			_infos.TryGetValue(id, out info);
			observed = _observers.ContainsKey(id);
		}

		if (info is null)
		{
			throw InputException.DeviceUnavailable($"HID-устройство {id} не найдено или отключено.");
		}

		if (observed)
		{
			throw InputException.DeviceBusy(
				$"HID-устройство {info.Path} читается для наблюдения за вводом: отключите HidBackendOptions.ObserveInputDevices, чтобы открыть его для прямого доступа.");
		}

		try
		{
			var connection = await Task.Run(() => _platform.Open(info.Path), cancellationToken).ConfigureAwait(false);
			return new HidChannel(connection, mode, info.Path, info.MaxInputReportLength);
		}
		catch (UnauthorizedAccessException ex)
		{
			throw new InputException(InputErrorCode.PermissionDenied, $"Нет прав на открытие HID-устройства {info.Path}.", ex)
			{
				Remediation = PermissionHint(),
			};
		}
		catch (IOException ex)
		{
			// HidSharp не различает причины отказа. Типичные: клавиатуру или мышь держит система (Windows),
			// устройство занято другой программой, нет прав (Linux, macOS).
			throw new InputException(
				InputErrorCode.DeviceBusy,
				$"Не удалось открыть HID-устройство {info.Path}: оно занято или недоступно приложению ({ex.Message}).",
				ex)
			{
				Remediation = PermissionHint(),
			};
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _stopped, 1) == 0)
		{
			await StopAsync(CancellationToken.None).ConfigureAwait(false);
		}
	}

	private static string PermissionHint() =>
		OperatingSystem.IsWindows() ? "Клавиатуры и мыши, которыми владеет система, открыть для обмена нельзя; закройте программу, которая держит устройство."
		: OperatingSystem.IsLinux() ? "Нужен доступ к /dev/hidraw*: правило udev с нужной группой или MODE=\"0660\" для этого VID/PID."
		: OperatingSystem.IsMacOS() ? "Для клавиатур и мыши macOS требует разрешение «Мониторинг ввода» (Конфиденциальность и безопасность)."
		: "Проверьте права доступа к устройству.";

	private async Task RescanAsync(IBackendContext context, ChannelReader<bool> signal, CancellationToken cancellationToken)
	{
		try
		{
			while (await signal.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
			{
				// Несколько сигналов подряд (событие и таймер) — один пересчёт.
				while (signal.TryRead(out _))
				{
				}

				await Task.Run(() => Refresh(context), CancellationToken.None).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			context.ReportFault(HidFailure.ToException(ex), recoverable: true);
		}
	}

	/// <summary>Сверяет список ОС с известным: сначала убирает исчезнувшие, затем добавляет новые.</summary>
	private void Refresh(IBackendContext context)
	{
		var current = _platform.GetDevices()
			.Where(info => _options.Filter?.Invoke(info) ?? true)
			.GroupBy(info => info.Path, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

		List<DeviceId> removed = [];
		List<HidObserver> stopped = [];
		lock (_gate)
		{
			foreach (var path in _ids.Keys.Where(p => !current.ContainsKey(p)).ToList())
			{
				var id = _ids[path];
				_ids.Remove(path);
				_infos.Remove(id);
				removed.Add(id);
				if (_observers.Remove(id, out var observer))
				{
					stopped.Add(observer);
				}
			}
		}

		foreach (var observer in stopped)
		{
			_ = observer.StopAsync();
		}

		foreach (var id in removed)
		{
			context.RemoveDevice(id);
		}

		foreach (var (path, info) in current.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			bool known;
			lock (_gate)
			{
				known = _ids.ContainsKey(path);
			}

			if (known)
			{
				continue;
			}

			var (observe, connection, decoder) = PrepareObservation(info);
			var id = context.AddDevice(Describe(info, observe));
			HidObserver? newObserver = null;
			if (connection is not null && decoder is not null)
			{
				newObserver = new HidObserver(connection, decoder, id, context, Math.Max(Math.Max(info.MaxInputReportLength, decoder.MaxReportLength), 64));
			}

			lock (_gate)
			{
				_ids[path] = id;
				_infos[id] = info;
				if (newObserver is not null)
				{
					_observers[id] = newObserver;
				}
			}

			newObserver?.Start();
		}
	}

	/// <summary>
	/// Решает, можно ли наблюдать устройство: опция включена, в дескрипторе есть клавиатура или мышь, и устройство открылось.
	/// Открытие делается заранее, чтобы объявлять Observe поддерживаемым только тогда, когда он действительно заработает.
	/// </summary>
	private (OperationSupport Observe, IHidConnection? Connection, HidInputDecoder? Decoder) PrepareObservation(HidDeviceInfo info)
	{
		if (!_options.ObserveInputDevices)
		{
			return (OperationSupport.Unsupported(
				"Чтение входных отчётов выключено: включите HidBackendOptions.ObserveInputDevices; иначе отчёты доступны только сырыми через прямой доступ."), null, null);
		}

		if (info.ReportDescriptor is null)
		{
			return (OperationSupport.Unsupported("Дескриптор отчётов устройства недоступен, разобрать отчёты нельзя."), null, null);
		}

		var decoder = HidInputDecoder.TryCreate(info.ReportDescriptor);
		if (decoder is null)
		{
			return (OperationSupport.Unsupported(
				"В дескрипторе нет клавиатуры или мыши, отчёты которых backend умеет разбирать в события ввода."), null, null);
		}

		try
		{
			return (OperationSupport.Supported, _platform.Open(info.Path), decoder);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InputException)
		{
			return (new OperationSupport(
				SupportState.Unsupported,
				$"Устройство не удалось открыть для чтения отчётов: {ex.Message}",
				PermissionHint()), null, null);
		}
	}

	private static DeviceDescriptor Describe(HidDeviceInfo info, OperationSupport observe)
	{
		Dictionary<string, string> properties = new()
		{
			["library"] = "HidSharp",
			["path"] = info.Path,
			["release"] = info.ReleaseNumber.ToString("X4"),
		};

		if (!string.IsNullOrEmpty(info.Manufacturer))
		{
			properties["manufacturer"] = info.Manufacturer;
		}

		if (info.Usages.Count > 0)
		{
			properties["usages"] = string.Join(',', info.Usages);
		}

		return new DeviceDescriptor
		{
			Key = new DeviceKey("hid:" + info.Path),
			Name = string.IsNullOrWhiteSpace(info.Product) ? $"HID {info.VendorId:X4}:{info.ProductId:X4}" : info.Product,
			Kind = HidDeviceClassifier.KindOf(info.Usages),
			Origin = DeviceOrigin.Physical,
			Bus = DeviceBus.Unknown,
			Identity = new DeviceIdentity(
				ToUShort(info.VendorId),
				ToUShort(info.ProductId),
				string.IsNullOrEmpty(info.SerialNumber) ? null : info.SerialNumber,
				info.Path),
			Capabilities = HidDeviceClassifier.CapabilitiesOf(info.Usages),
			Operations = new Dictionary<InputOperation, OperationSupport>
			{
				[InputOperation.Enumerate] = OperationSupport.Supported,
				[InputOperation.DirectAccess] = OperationSupport.Supported,
				[InputOperation.Observe] = observe,
				[InputOperation.Emulate] = OperationSupport.Unsupported("Эмуляция ввода через HID-устройство не поддерживается."),
			},
			Properties = properties,
		};
	}

	private static ushort? ToUShort(int value) => value is >= 0 and <= ushort.MaxValue ? (ushort)value : null;
}

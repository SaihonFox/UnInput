using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalInput.Core.Backends;
using UniversalInput.Core.Configuration;
using UniversalInput.Core.Devices;
using UniversalInput.Core.Diagnostics;
using UniversalInput.Core.Events;
using UniversalInput.Core.Raw;

namespace UniversalInput.Core.Hub;

/// <summary>
/// Реализация <see cref="IInputHub"/>: реестр устройств, конвейер событий, подписки и жизненный цикл backend.
/// Одноразовый: <see cref="StartAsync"/> и <see cref="StopAsync"/> вызываются по одному разу.
/// </summary>
public sealed class InputHub : IInputHub, IAsyncDisposable
{
	private const int NotStarted = 0;
	private const int Started = 1;
	private const int Stopped = 2;

	private readonly InputHubOptions _options;
	private readonly ILogger _logger;
	private readonly InputMetrics _metrics = new();
	private readonly DeviceRegistry _registry;
	private readonly BackendRunner[] _runners;
	private readonly BackendRunner[] _byPriority;
	private readonly EventDispatcher _dispatcher;
	private readonly ConcurrentDictionary<DeviceId, RawChannelHandle?> _rawChannels = new();
	private int _lifecycle;
	private int _disposed;

	public InputHub(
		IEnumerable<BackendRegistration> backends,
		InputHubOptions? options = null,
		TimeProvider? timeProvider = null,
		ILogger<InputHub>? logger = null)
	{
		ArgumentNullException.ThrowIfNull(backends);

		_options = options ?? new InputHubOptions();
		_options.Validate();
		_logger = logger ?? NullLogger<InputHub>.Instance;
		var time = timeProvider ?? TimeProvider.System;

		var registrations = backends.ToArray();
		if (registrations.Length > ushort.MaxValue)
		{
			throw new ArgumentException("Слишком много backend.", nameof(backends));
		}

		_registry = new DeviceRegistry(time, _options.RemovedDeviceRetention, new RolePriority(_options));

		var indexes = new Dictionary<string, ushort>(StringComparer.Ordinal);
		var queues = new BackendQueue[registrations.Length];
		for (var i = 0; i < registrations.Length; i++)
		{
			var id = registrations[i].Backend.Id;
			if (string.IsNullOrWhiteSpace(id))
			{
				throw new ArgumentException("У backend пустой Id.", nameof(backends));
			}

			var index = (ushort)i;
			if (!indexes.TryAdd(id, index))
			{
				throw new ArgumentException($"Backend с Id '{id}' зарегистрирован дважды.", nameof(backends));
			}

			queues[i] = new BackendQueue(index, id, _options.BackendQueueCapacity);
		}

		_dispatcher = new EventDispatcher(queues, _registry, _metrics, indexes, time);

		_runners = new BackendRunner[registrations.Length];
		for (var i = 0; i < registrations.Length; i++)
		{
			_runners[i] = new BackendRunner(
				registrations[i], (ushort)i, queues[i], _registry, _options, _metrics, time,
				_dispatcher.Wake, OnDeviceRemoved, _logger);
		}

		_byPriority = OrderByPriority(_runners, indexes, _options.BackendPriority);

		_metrics.ObserveQueueDepth(() => queues.Select(q =>
			new Measurement<int>(q.Count, new KeyValuePair<string, object?>("backend", q.BackendId))));
	}

	public IReadOnlyList<InputDevice> Devices => _registry.Snapshot;

	public IReadOnlyList<BackendStatus> Backends => _runners.Select(r => r.GetStatus()).ToArray();

	/// <summary>Запускает диспетчер и backend. Ошибки backend отражаются в <see cref="Backends"/>, кроме режима FailFast.</summary>
	/// <exception cref="InputException">Режим FailFast: не запустился обязательный backend.</exception>
	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed != 0, this);

		if (Interlocked.CompareExchange(ref _lifecycle, Started, NotStarted) != NotStarted)
		{
			throw new InvalidOperationException("Хаб уже запущен или остановлен.");
		}

		_dispatcher.Start();
		await Task.WhenAll(_runners.Select(r => r.StartAsync(cancellationToken))).ConfigureAwait(false);

		foreach (var runner in _runners)
		{
			if (!runner.Required || runner.IsRunning)
			{
				continue;
			}

			var status = runner.GetStatus();
			_logger.LogError("Обязательный backend {BackendId} не запущен: {Reason}", runner.Id, status.Reason);

			if (_options.FailFast)
			{
				throw new InputException(
					status.ErrorCode ?? InputErrorCode.BackendUnavailable,
					$"Обязательный backend '{runner.Id}' не запущен: {status.Reason}")
				{
					Remediation = status.Remediation,
				};
			}
		}
	}

	/// <summary>Останавливает backend, выбирает оставшиеся события (подписчики увидят отключения устройств) и завершает подписки.</summary>
	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		if (Interlocked.Exchange(ref _lifecycle, Stopped) == Stopped)
		{
			return;
		}

		await Task.WhenAll(_runners.Select(r => r.StopAsync(cancellationToken))).ConfigureAwait(false);
		await _dispatcher.StopAsync().ConfigureAwait(false);

		foreach (var channel in _rawChannels.Values)
		{
			channel?.Close();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await StopAsync().ConfigureAwait(false);

		foreach (var runner in _runners)
		{
			await runner.DisposeAsync().ConfigureAwait(false);
		}

		_metrics.Dispose();
	}

	public IInputSubscription Subscribe(SubscriptionOptions? options = null)
	{
		ObjectDisposedException.ThrowIf(_disposed != 0, this);
		return _dispatcher.Subscribe(options ?? new SubscriptionOptions());
	}

	/// <summary>Подписка создаётся при начале перечисления: события до него не видны.</summary>
	public async IAsyncEnumerable<InputEvent> ReadEventsAsync(
		SubscriptionOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await using var subscription = Subscribe(options);
		await foreach (var inputEvent in subscription.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
		{
			yield return inputEvent;
		}
	}

	public OperationSupport GetSupport(InputOperation operation, DeviceId? device = null)
	{
		if (device is { } id && !id.IsNone)
		{
			return GetDeviceSupport(operation, id);
		}

		OperationSupport best = default;
		var any = false;
		foreach (var runner in _byPriority)
		{
			var support = GetBackendSupport(runner, operation);
			if (support.IsSupported)
			{
				return support;
			}

			if (!any || support.State < best.State)
			{
				best = support;
				any = true;
			}
		}

		return any ? best : OperationSupport.Unsupported("Не зарегистрировано ни одного backend.");
	}

	public async ValueTask EmulateAsync(InputEvent inputEvent, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var runner = SelectRunner(InputOperation.Emulate, inputEvent.Device);
		if (!runner.TryGetFeature<IInputEmulator>(out var emulator))
		{
			throw InputException.Unsupported($"Backend '{runner.Id}' не поддерживает эмуляцию.");
		}

		var simulated = inputEvent with { Flags = inputEvent.Flags | InputEventFlags.Simulated };
		await emulator.EmulateAsync(simulated, cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask PlayHapticAsync(DeviceId id, HapticEffect effect, CancellationToken cancellationToken = default)
	{
		if (id.IsNone)
		{
			throw new ArgumentException("Нужно указать устройство.", nameof(id));
		}

		cancellationToken.ThrowIfCancellationRequested();

		var runner = SelectRunner(InputOperation.Output, id);
		if (!runner.TryGetFeature<IHapticOutput>(out var haptic))
		{
			throw InputException.Unsupported($"Backend '{runner.Id}' не поддерживает вибрацию.");
		}

		await haptic.PlayAsync(id, effect, cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask<IRawDeviceChannel> OpenRawAsync(
		DeviceId id, RawAccessMode mode, CancellationToken cancellationToken = default)
	{
		if (id.IsNone)
		{
			throw new ArgumentException("Нужно указать устройство.", nameof(id));
		}

		cancellationToken.ThrowIfCancellationRequested();

		var runner = SelectRunner(InputOperation.DirectAccess, id);
		if (!_rawChannels.TryAdd(id, null))
		{
			throw InputException.DeviceBusy($"Устройство {id} уже открыто для прямого доступа.");
		}

		try
		{
			if (!runner.TryGetFeature<IRawDeviceAccess>(out var access))
			{
				throw InputException.Unsupported($"Backend '{runner.Id}' не поддерживает прямой доступ.");
			}

			var inner = await access.OpenAsync(id, mode, cancellationToken).ConfigureAwait(false);
			var handle = new RawChannelHandle(inner, runner.Index, () => _rawChannels.TryRemove(id, out _));
			_rawChannels[id] = handle;

			// Устройство могло пропасть или остаться без этого поставщика, пока канал открывался.
			if (!_registry.TryGetLive(id, out var entry)
				|| !entry.State.TryGetOwner(InputOperation.DirectAccess, out var owner)
				|| owner != runner.Index)
			{
				handle.Close();
				throw InputException.DeviceUnavailable($"Устройство {id} отключилось во время открытия.");
			}

			return handle;
		}
		catch
		{
			_rawChannels.TryRemove(id, out _);
			throw;
		}
	}

	/// <summary>Канал закрывается, только если исчез поставщик, через которого он открыт.</summary>
	private void OnDeviceRemoved(DeviceId id, ushort backend)
	{
		if (_rawChannels.TryGetValue(id, out var handle) && handle is not null && handle.Backend == backend)
		{
			handle.Close();
		}
	}

	private BackendRunner SelectRunner(InputOperation operation, DeviceId device)
	{
		if (!device.IsNone)
		{
			if (!_registry.TryGetLive(device, out var entry))
			{
				throw InputException.DeviceUnavailable($"Устройство {device} не найдено или отключено.");
			}

			var deviceSupport = GetDeviceSupport(operation, device);
			if (!deviceSupport.IsSupported)
			{
				throw ToException(operation, deviceSupport);
			}

			if (!entry.State.TryGetOwner(operation, out var owner))
			{
				throw InputException.Unsupported($"Для устройства {device} нет backend, выполняющего операцию {operation}.");
			}

			return _runners[owner];
		}

		foreach (var runner in _byPriority)
		{
			if (GetBackendSupport(runner, operation).IsSupported)
			{
				return runner;
			}
		}

		throw ToException(operation, GetSupport(operation));
	}

	private OperationSupport GetDeviceSupport(InputOperation operation, DeviceId id)
	{
		if (!_registry.TryGetLive(id, out var entry))
		{
			return OperationSupport.Unsupported($"Устройство {id} не найдено или отключено.");
		}

		if (operation == InputOperation.Enumerate)
		{
			return OperationSupport.Supported;
		}

		var state = entry.State;

		// Роль исполняет её владелец; если роль никто не поддерживает, причину сообщает основной поставщик.
		var owned = state.TryGetOwner(operation, out var ownerIndex);
		var runner = _runners[owned ? ownerIndex : state.ObserveOwner];
		if (!runner.IsRunning)
		{
			return GetBackendSupport(runner, operation);
		}

		if (!state.Device.Operations.TryGetValue(operation, out var declared))
		{
			return OperationSupport.Unsupported($"Backend '{runner.Id}' не объявил операцию {operation} для этого устройства.");
		}

		if (!declared.IsSupported)
		{
			return declared;
		}

		return HasFeature(runner, operation)
			? declared
			: OperationSupport.Unsupported($"Backend '{runner.Id}' не реализует возможность для операции {operation}.");
	}

	private static OperationSupport GetBackendSupport(BackendRunner runner, InputOperation operation)
	{
		if (!runner.IsRunning)
		{
			var status = runner.GetStatus();
			return status.ErrorCode switch
			{
				InputErrorCode.PermissionDenied =>
					new OperationSupport(SupportState.RequiresPermission, status.Reason, status.Remediation),
				InputErrorCode.PlatformRestriction =>
					new OperationSupport(SupportState.RestrictedByPlatform, status.Reason, status.Remediation),
				_ => new OperationSupport(
					SupportState.Unsupported,
					status.Reason ?? $"Backend '{runner.Id}' не запущен (состояние: {status.State}).",
					status.Remediation),
			};
		}

		return HasFeature(runner, operation)
			? OperationSupport.Supported
			: OperationSupport.Unsupported($"Backend '{runner.Id}' не поддерживает операцию {operation}.");
	}

	private static bool HasFeature(BackendRunner runner, InputOperation operation) => operation switch
	{
		InputOperation.Emulate => runner.TryGetFeature<IInputEmulator>(out _),
		InputOperation.Intercept => runner.TryGetFeature<IEventInterceptor>(out _),
		InputOperation.DirectAccess => runner.TryGetFeature<IRawDeviceAccess>(out _),
		InputOperation.Output => runner.TryGetFeature<IHapticOutput>(out _),
		_ => true,
	};

	private static InputException ToException(InputOperation operation, OperationSupport support)
	{
		var code = support.State switch
		{
			SupportState.RequiresPermission => InputErrorCode.PermissionDenied,
			SupportState.RestrictedByPlatform => InputErrorCode.PlatformRestriction,
			_ => InputErrorCode.UnsupportedOperation,
		};

		return new InputException(code, $"Операция {operation} недоступна: {support.Reason ?? support.State.ToString()}")
		{
			Remediation = support.Remediation,
		};
	}

	private static BackendRunner[] OrderByPriority(
		BackendRunner[] runners, Dictionary<string, ushort> indexes, IEnumerable<string> priority)
	{
		List<BackendRunner> ordered = [];
		foreach (var id in priority)
		{
			if (indexes.TryGetValue(id, out var index) && !ordered.Contains(runners[index]))
			{
				ordered.Add(runners[index]);
			}
		}

		foreach (var runner in runners)
		{
			if (!ordered.Contains(runner))
			{
				ordered.Add(runner);
			}
		}

		return [.. ordered];
	}
}

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using UniversalInput.Core.Configuration;
using UniversalInput.Core.Devices;
using UniversalInput.Core.Diagnostics;

namespace UniversalInput.Core.Backends;

/// <summary>
/// Ведёт один backend по машине состояний: Created → Probing → Ready/Unavailable → Starting → Running →
/// Faulted → Starting (перезапуск с экспоненциальной задержкой) или Stopped; Stopping → Stopped → Disposed.
/// </summary>
internal sealed class BackendRunner : IAsyncDisposable
{
	private readonly object _gate = new();
	private readonly InputHubOptions _options;
	private readonly Action _wake;
	private readonly Action<DeviceId, ushort> _deviceRemoved;
	private readonly ILogger _logger;
	private readonly CancellationTokenSource _lifetime = new();

	private BackendState _state = BackendState.Created;
	private InputErrorCode? _errorCode;
	private string? _reason;
	private string? _remediation;
	private int _restartCount;
	private int _attempt;
	private DateTimeOffset _runningSince;
	private BackendContext? _context;
	private Task _recovery = Task.CompletedTask;

	public BackendRunner(
		BackendRegistration registration,
		ushort index,
		BackendQueue queue,
		DeviceRegistry registry,
		InputHubOptions options,
		InputMetrics metrics,
		TimeProvider time,
		Action wake,
		Action<DeviceId, ushort> deviceRemoved,
		ILogger logger)
	{
		Backend = registration.Backend;
		Required = registration.Required;
		Index = index;
		Queue = queue;
		Registry = registry;
		Metrics = metrics;
		Time = time;
		_options = options;
		_wake = wake;
		_deviceRemoved = deviceRemoved;
		_logger = logger;
	}

	public IInputBackend Backend { get; }

	public bool Required { get; }

	public ushort Index { get; }

	public string Id => Backend.Id;

	public BackendQueue Queue { get; }

	public DeviceRegistry Registry { get; }

	public InputMetrics Metrics { get; }

	public TimeProvider Time { get; }

	public bool IsRunning
	{
		get
		{
			lock (_gate)
			{
				return _state == BackendState.Running;
			}
		}
	}

	public void Wake() => _wake();

	/// <summary>Backend перестал поставлять устройство (само устройство при этом могло остаться у другого backend).</summary>
	public void OnDeviceRemoved(DeviceId id) => _deviceRemoved(id, Index);

	/// <summary>Возможность backend; доступна, только пока backend запущен.</summary>
	public bool TryGetFeature<TFeature>([NotNullWhen(true)] out TFeature? feature)
		where TFeature : class, IBackendFeature
	{
		if (!IsRunning)
		{
			feature = null;
			return false;
		}

		return Backend.TryGetFeature(out feature);
	}

	public BackendStatus GetStatus()
	{
		BackendState state;
		InputErrorCode? code;
		string? reason;
		string? remediation;
		int restarts;
		lock (_gate)
		{
			state = _state;
			code = _errorCode;
			reason = _reason;
			remediation = _remediation;
			restarts = _restartCount;
		}

		List<InputOperation> roles = [];
		if (state == BackendState.Running)
		{
			roles.Add(InputOperation.Enumerate);
			roles.Add(InputOperation.Observe);
			if (Backend.TryGetFeature<IInputEmulator>(out _)) roles.Add(InputOperation.Emulate);
			if (Backend.TryGetFeature<IEventInterceptor>(out _)) roles.Add(InputOperation.Intercept);
			if (Backend.TryGetFeature<IRawDeviceAccess>(out _)) roles.Add(InputOperation.DirectAccess);
			if (Backend.TryGetFeature<IHapticOutput>(out _)) roles.Add(InputOperation.Output);
		}

		return new BackendStatus
		{
			Id = Id,
			Index = Index,
			State = state,
			ErrorCode = code,
			Reason = reason,
			Remediation = remediation,
			RestartCount = restarts,
			Roles = roles,
		};
	}

	/// <summary>Проверяет и запускает backend. Ошибки backend не выбрасываются, а отражаются в статусе; <c>true</c> — backend работает.</summary>
	public async Task<bool> StartAsync(CancellationToken cancellationToken)
	{
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		var token = linked.Token;

		Transition(BackendState.Probing);

		BackendProbe probe;
		try
		{
			probe = await Backend.ProbeAsync(token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			SetUnavailable(CodeOf(ex), ex.Message, (ex as InputException)?.Remediation);
			_logger.LogWarning(ex, "Backend {BackendId}: проверка завершилась ошибкой.", Id);
			return false;
		}

		if (!probe.IsAvailable)
		{
			SetUnavailable(probe.FailureCode ?? InputErrorCode.BackendUnavailable, probe.Reason, probe.Remediation);
			_logger.LogWarning("Backend {BackendId} недоступен: {Reason}", Id, probe.Reason);
			return false;
		}

		Transition(BackendState.Ready);

		switch (await StartCoreAsync(token).ConfigureAwait(false))
		{
			case StartOutcome.Running:
				return true;

			case StartOutcome.Recoverable:
				lock (_gate)
				{
					_recovery = Task.Run(() => RecoverAsync(alreadyStopped: true));
				}

				return false;

			default:
				return false;
		}
	}

	/// <summary>Останавливает backend: прекращает перезапуски, вызывает StopAsync с тайм-аутом и убирает устройства.</summary>
	public async Task StopAsync(CancellationToken cancellationToken)
	{
		_lifetime.Cancel();

		try
		{
			await _recovery.ConfigureAwait(false);
		}
		catch (Exception)
		{
			// Ошибки восстановления уже записаны в журнал.
		}

		bool needStop;
		BackendContext? context;
		lock (_gate)
		{
			needStop = _state is BackendState.Running or BackendState.Starting
				or BackendState.Ready or BackendState.Faulted or BackendState.Probing;
			context = _context;
			if (needStop)
			{
				_state = BackendState.Stopping;
			}
		}

		if (!needStop)
		{
			return;
		}

		context?.Deactivate();
		await StopBackendQuietlyAsync(cancellationToken).ConfigureAwait(false);
		RemoveAllDevices();

		lock (_gate)
		{
			_state = BackendState.Stopped;
		}
	}

	public async ValueTask DisposeAsync()
	{
		try
		{
			await Backend.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Backend {BackendId}: ошибка при освобождении.", Id);
		}

		lock (_gate)
		{
			_state = BackendState.Disposed;
		}

		_lifetime.Dispose();
	}

	/// <summary>Вызывается контекстом, когда backend сообщает о сбое.</summary>
	public void OnFault(BackendContext context, InputException error, bool recoverable)
	{
		lock (_gate)
		{
			if (!ReferenceEquals(_context, context))
			{
				return;
			}

			if (_state == BackendState.Starting)
			{
				context.RecordStartFault(error, recoverable);
				return;
			}

			if (_state != BackendState.Running)
			{
				return;
			}

			_state = BackendState.Faulted;
			_errorCode = error.Code;
			_reason = error.Message;
			_remediation = error.Remediation;
			context.Deactivate();

			if (Time.GetUtcNow() - _runningSince >= _options.RestartMaxDelay)
			{
				_attempt = 0;
			}

			_recovery = recoverable
				? Task.Run(() => RecoverAsync(alreadyStopped: false))
				: Task.Run(StopAfterFatalFaultAsync);
		}

		_logger.LogError(error, "Backend {BackendId} сообщил о сбое (восстановимый: {Recoverable}).", Id, recoverable);
	}

	private async Task<StartOutcome> StartCoreAsync(CancellationToken token)
	{
		var context = new BackendContext(this);
		lock (_gate)
		{
			_context = context;
			_state = BackendState.Starting;
		}

		Exception? failure = null;
		var recoverable = true;
		try
		{
			await Backend.StartAsync(context, token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			context.Deactivate();
			throw;
		}
		catch (Exception ex)
		{
			failure = ex;
			recoverable = IsRecoverable(ex);
		}

		if (failure is null && context.TakeStartFault() is { } startFault)
		{
			failure = startFault.Error;
			recoverable = startFault.Recoverable;
		}

		if (failure is null)
		{
			lock (_gate)
			{
				if (!ReferenceEquals(_context, context) || _state != BackendState.Starting)
				{
					return StartOutcome.Fatal;
				}

				_state = BackendState.Running;
				_errorCode = null;
				_reason = null;
				_remediation = null;
				_runningSince = Time.GetUtcNow();
			}

			_logger.LogInformation("Backend {BackendId} запущен.", Id);
			return StartOutcome.Running;
		}

		context.Deactivate();
		await StopBackendQuietlyAsync(CancellationToken.None).ConfigureAwait(false);
		RemoveAllDevices();

		lock (_gate)
		{
			_state = recoverable ? BackendState.Faulted : BackendState.Unavailable;
			_errorCode = CodeOf(failure);
			_reason = failure.Message;
			_remediation = (failure as InputException)?.Remediation;
		}

		_logger.LogWarning(failure, "Backend {BackendId} не удалось запустить (восстановимая ошибка: {Recoverable}).", Id, recoverable);
		return recoverable ? StartOutcome.Recoverable : StartOutcome.Fatal;
	}

	private async Task RecoverAsync(bool alreadyStopped)
	{
		var token = _lifetime.Token;
		try
		{
			if (!alreadyStopped)
			{
				await StopBackendQuietlyAsync(CancellationToken.None).ConfigureAwait(false);
				RemoveAllDevices();
			}

			while (!token.IsCancellationRequested)
			{
				await Task.Delay(NextDelay(), Time, token).ConfigureAwait(false);

				lock (_gate)
				{
					_restartCount++;
				}

				if (await StartCoreAsync(token).ConfigureAwait(false) != StartOutcome.Recoverable)
				{
					return;
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Backend {BackendId}: ошибка при перезапуске.", Id);
		}
	}

	private async Task StopAfterFatalFaultAsync()
	{
		await StopBackendQuietlyAsync(CancellationToken.None).ConfigureAwait(false);
		RemoveAllDevices();

		lock (_gate)
		{
			if (_state == BackendState.Faulted)
			{
				_state = BackendState.Stopped;
			}
		}
	}

	private TimeSpan NextDelay()
	{
		int attempt;
		lock (_gate)
		{
			attempt = _attempt++;
		}

		var factor = Math.Pow(2, Math.Min(attempt, 20));
		var ticks = Math.Min((double)_options.RestartMaxDelay.Ticks, _options.RestartMinDelay.Ticks * factor);
		return TimeSpan.FromTicks((long)ticks);
	}

	private async Task StopBackendQuietlyAsync(CancellationToken cancellationToken)
	{
		using var timeout = new CancellationTokenSource(_options.BackendStopTimeout, Time);
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
		try
		{
			await Backend.StopAsync(linked.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (linked.IsCancellationRequested)
		{
			_logger.LogWarning(
				"Backend {BackendId} не остановился за {Timeout}; освобождается принудительно.",
				Id, _options.BackendStopTimeout);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Backend {BackendId}: ошибка при остановке.", Id);
		}
	}

	private void RemoveAllDevices()
	{
		List<DeviceId> survivors = [];
		foreach (var device in Registry.RemoveAll(Index, survivors))
		{
			_deviceRemoved(device.Id, Index);
			var disconnected = InputEvent.Disconnected(device.Id);
			Queue.TryWrite(in disconnected);
		}

		foreach (var id in survivors)
		{
			_deviceRemoved(id, Index);
		}

		_wake();
	}

	private void Transition(BackendState state)
	{
		lock (_gate)
		{
			_state = state;
		}
	}

	private void SetUnavailable(InputErrorCode code, string? reason, string? remediation)
	{
		lock (_gate)
		{
			_state = BackendState.Unavailable;
			_errorCode = code;
			_reason = reason;
			_remediation = remediation;
		}
	}

	private static InputErrorCode CodeOf(Exception exception) =>
		exception is InputException input ? input.Code : InputErrorCode.BackendFault;

	/// <summary>Сбой backend и недоступность устройства лечатся перезапуском; отсутствие прав и ограничения платформы — нет.</summary>
	private static bool IsRecoverable(Exception exception) =>
		exception is not InputException input
		|| input.Code is InputErrorCode.BackendFault or InputErrorCode.DeviceUnavailable or InputErrorCode.DeviceBusy;

	private enum StartOutcome
	{
		Running,
		Recoverable,
		Fatal,
	}
}

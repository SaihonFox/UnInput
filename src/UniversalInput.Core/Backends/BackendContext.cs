namespace UniversalInput.Core.Backends;

/// <summary>
/// Контекст одного запуска backend. После остановки или сбоя деактивируется: «запоздавшие» вызовы
/// из потоков старого запуска не попадают в конвейер.
/// </summary>
internal sealed class BackendContext : IBackendContext
{
	private readonly BackendRunner _runner;
	private readonly object _gate = new();
	private volatile bool _active = true;
	private (InputException Error, bool Recoverable)? _startFault;

	public BackendContext(BackendRunner runner) => _runner = runner;

	public TimeProvider TimeProvider => _runner.Time;

	public DeviceId AddDevice(DeviceDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		if (!_active)
		{
			throw InputException.BackendUnavailable($"Backend '{_runner.Id}' не запущен.");
		}

		var device = _runner.Registry.Add(_runner.Index, _runner.Id, descriptor, out var created);
		if (created)
		{
			// Устройство, к которому присоединился второй backend, уже объявлено: повторного DeviceConnected не будет.
			var connected = InputEvent.Connected(device.Id);
			Enqueue(in connected);
		}

		return device.Id;
	}

	public void RemoveDevice(DeviceId id)
	{
		if (!_active)
		{
			return;
		}

		if (_runner.Registry.Remove(id, _runner.Index, out _, out var deviceGone))
		{
			_runner.OnDeviceRemoved(id);
			if (deviceGone)
			{
				// Если устройство осталось благодаря другому backend, подписчики отключения не видят.
				var disconnected = InputEvent.Disconnected(id);
				Enqueue(in disconnected);
			}
		}
	}

	public bool TryWrite(in InputEvent inputEvent)
	{
		if (!_active)
		{
			_runner.Metrics.Dropped(_runner.Id, "inactive_backend");
			return false;
		}

		return Enqueue(in inputEvent);
	}

	public void ReportFault(InputException error, bool recoverable)
	{
		ArgumentNullException.ThrowIfNull(error);

		if (_active)
		{
			_runner.OnFault(this, error, recoverable);
		}
	}

	public void Deactivate() => _active = false;

	/// <summary>Сбой, о котором backend сообщил во время StartAsync: запуск считается неудачным.</summary>
	public void RecordStartFault(InputException error, bool recoverable)
	{
		lock (_gate)
		{
			_startFault ??= (error, recoverable);
		}
	}

	public (InputException Error, bool Recoverable)? TakeStartFault()
	{
		lock (_gate)
		{
			return _startFault;
		}
	}

	private bool Enqueue(in InputEvent inputEvent)
	{
		var written = _runner.Queue.TryWrite(in inputEvent);
		if (written)
		{
			_runner.Metrics.Received(_runner.Id);
		}

		// Будим диспетчер и при переполнении: он должен выдать StreamGap.
		_runner.Wake();
		return written;
	}
}

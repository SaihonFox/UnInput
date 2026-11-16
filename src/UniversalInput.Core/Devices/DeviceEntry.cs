namespace UniversalInput.Core.Devices;

/// <summary>Запись реестра: состояние логического устройства и признак «подключено».</summary>
internal sealed class DeviceEntry
{
	private volatile DeviceState _state;
	private volatile ushort[] _everProviders = [];

	public DeviceEntry(DeviceState state)
	{
		_state = state;
		Live = true;
	}

	/// <summary>Читается без блокировки, заменяется целиком под блокировкой реестра.</summary>
	public DeviceState State
	{
		get => _state;
		set => _state = value;
	}

	public InputDevice Device => _state.Device;

	/// <summary>Читается без блокировки, меняется под блокировкой реестра.</summary>
	public volatile bool Live;

	public DateTimeOffset RemovedAt { get; set; }

	/// <summary>Запоминает backend, который поставлял это устройство. Вызывается под блокировкой реестра.</summary>
	public void Remember(ushort backend)
	{
		var current = _everProviders;
		if (Array.IndexOf(current, backend) < 0)
		{
			_everProviders = [.. current, backend];
		}
	}

	/// <summary>
	/// Поставлял ли backend это устройство (хотя бы раз с момента появления записи). Нужно диспетчеру: события
	/// DeviceConnected и DeviceDisconnected приходят из очереди backend и могут быть обработаны уже после его ухода.
	/// </summary>
	public bool WasProvider(ushort backend) => Array.IndexOf(_everProviders, backend) >= 0;
}

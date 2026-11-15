namespace UniversalInput.Core.Devices;

/// <summary>Запись реестра: устройство, владеющий им backend и признак «подключено».</summary>
internal sealed class DeviceEntry
{
	public DeviceEntry(InputDevice device, ushort backend)
	{
		Device = device;
		Backend = backend;
		Live = true;
	}

	public InputDevice Device { get; set; }

	public ushort Backend { get; }

	/// <summary>Читается без блокировки, меняется под блокировкой реестра.</summary>
	public volatile bool Live;

	public DateTimeOffset RemovedAt { get; set; }
}

namespace UniversalInput.Abstractions.Backends;

/// <summary>То, что Core даёт backend: добавление и удаление устройств и запись событий.</summary>
public interface IBackendContext
{
	TimeProvider TimeProvider { get; }

	/// <summary>Добавляет устройство; Core присваивает <see cref="DeviceId"/> и выдаёт DeviceConnected.</summary>
	DeviceId AddDevice(DeviceDescriptor descriptor);

	/// <summary>Удаляет устройство; Core закрывает каналы и выдаёт DeviceDisconnected.</summary>
	void RemoveDevice(DeviceId id);

	/// <summary>Не блокирует и вызывается из любого потока. <c>false</c> — событие отброшено и посчитано.</summary>
	bool TryWrite(in InputEvent inputEvent);

	/// <summary>Сообщает об ошибке. Восстановимая — Core перезапустит backend, невосстановимая — остановит.</summary>
	void ReportFault(InputException error, bool recoverable);
}

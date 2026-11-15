using System.Collections.Concurrent;

namespace UniversalInput.Core.Devices;

/// <summary>
/// Реестр устройств. Изменения идут под блокировкой, чтение (включая горячий путь диспетчера) — без неё.
/// Удалённое устройство помнится заданное время: при возвращении с тем же <see cref="DeviceKey"/> оно получает прежний <see cref="DeviceId"/>.
/// </summary>
internal sealed class DeviceRegistry
{
	private readonly object _gate = new();
	private readonly TimeProvider _time;
	private readonly TimeSpan _retention;
	private readonly ConcurrentDictionary<DeviceId, DeviceEntry> _entries = new();
	private readonly Dictionary<DeviceKey, DeviceId> _ids = new();
	private InputDevice[] _snapshot = [];
	private int _nextId;

	public DeviceRegistry(TimeProvider time, TimeSpan retention)
	{
		_time = time;
		_retention = retention;
	}

	/// <summary>Подключённые сейчас устройства, по возрастанию <see cref="DeviceId"/>.</summary>
	public IReadOnlyList<InputDevice> Snapshot => Volatile.Read(ref _snapshot);

	/// <summary>Возвращает запись, в том числе об устройстве, удалённом недавно.</summary>
	public bool TryGet(DeviceId id, out DeviceEntry entry) => _entries.TryGetValue(id, out entry!);

	public bool TryGetLive(DeviceId id, out DeviceEntry entry) =>
		_entries.TryGetValue(id, out entry!) && entry.Live;

	/// <exception cref="InputException">Ключ уже занят подключённым устройством.</exception>
	public InputDevice Add(ushort backend, string backendId, DeviceDescriptor descriptor)
	{
		lock (_gate)
		{
			PurgeExpired();

			DeviceEntry entry;
			if (_ids.TryGetValue(descriptor.Key, out var existingId))
			{
				var existing = _entries[existingId];
				if (existing.Live)
				{
					throw InputException.DeviceBusy(
						$"Устройство с ключом '{descriptor.Key}' уже зарегистрировано backend '{existing.Device.BackendId}'.");
				}

				entry = new DeviceEntry(Create(existingId, backendId, descriptor), backend);
			}
			else
			{
				var id = new DeviceId(++_nextId);
				_ids[descriptor.Key] = id;
				entry = new DeviceEntry(Create(id, backendId, descriptor), backend);
			}

			_entries[entry.Device.Id] = entry;
			RefreshSnapshot();
			return entry.Device;
		}
	}

	/// <summary>Помечает устройство удалённым. <c>false</c>, если его нет, оно уже удалено или принадлежит другому backend.</summary>
	public bool Remove(DeviceId id, ushort backend, out InputDevice device)
	{
		lock (_gate)
		{
			if (_entries.TryGetValue(id, out var entry) && entry.Live && entry.Backend == backend)
			{
				MarkRemoved(entry);
				RefreshSnapshot();
				device = entry.Device;
				return true;
			}

			device = null!;
			return false;
		}
	}

	/// <summary>Удаляет все устройства backend и возвращает их.</summary>
	public IReadOnlyList<InputDevice> RemoveAll(ushort backend)
	{
		lock (_gate)
		{
			List<InputDevice> removed = [];
			foreach (var entry in _entries.Values)
			{
				if (entry.Live && entry.Backend == backend)
				{
					MarkRemoved(entry);
					removed.Add(entry.Device);
				}
			}

			if (removed.Count > 0)
			{
				RefreshSnapshot();
			}

			return removed;
		}
	}

	private static InputDevice Create(DeviceId id, string backendId, DeviceDescriptor descriptor) => new()
	{
		Id = id,
		Key = descriptor.Key,
		Name = descriptor.Name,
		Kind = descriptor.Kind,
		Origin = descriptor.Origin,
		BackendId = backendId,
		Bus = descriptor.Bus,
		Identity = descriptor.Identity,
		Capabilities = descriptor.Capabilities,
		Operations = descriptor.Operations,
		Properties = descriptor.Properties,
	};

	private void MarkRemoved(DeviceEntry entry)
	{
		entry.RemovedAt = _time.GetUtcNow();
		entry.Live = false;
	}

	private void RefreshSnapshot()
	{
		var devices = _entries.Values
			.Where(e => e.Live)
			.Select(e => e.Device)
			.OrderBy(d => d.Id.Value)
			.ToArray();
		Volatile.Write(ref _snapshot, devices);
	}

	private void PurgeExpired()
	{
		var now = _time.GetUtcNow();
		foreach (var entry in _entries.Values)
		{
			if (!entry.Live && now - entry.RemovedAt > _retention)
			{
				_entries.TryRemove(entry.Device.Id, out _);
				_ids.Remove(entry.Device.Key);
			}
		}
	}
}

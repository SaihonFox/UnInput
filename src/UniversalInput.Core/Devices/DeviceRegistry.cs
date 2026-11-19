using System.Collections.Concurrent;

namespace UniversalInput.Core.Devices;

/// <summary>
/// Реестр логических устройств. Изменения идут под блокировкой, чтение (включая горячий путь диспетчера) — без неё.
/// <para>
/// Дескрипторы разных backend с одинаковыми VendorId, ProductId, серийным номером и шиной объединяются в одно логическое
/// устройство с одним <see cref="DeviceId"/>; для каждой роли владельцем становится один поставщик (см. <see cref="RolePriority"/>).
/// </para>
/// <para>
/// Удалённое устройство помнится заданное время: при возвращении с тем же <see cref="DeviceKey"/> оно получает прежний <see cref="DeviceId"/>.
/// </para>
/// </summary>
internal sealed class DeviceRegistry
{
	private static readonly InputOperation[] RoleOperations =
	[
		InputOperation.Observe,
		InputOperation.Emulate,
		InputOperation.Intercept,
		InputOperation.DirectAccess,
		InputOperation.Output,
	];

	private readonly object _gate = new();
	private readonly TimeProvider _time;
	private readonly TimeSpan _retention;
	private readonly RolePriority _priority;
	private readonly ConcurrentDictionary<DeviceId, DeviceEntry> _entries = new();
	private readonly Dictionary<DeviceKey, DeviceId> _ids = new();
	private InputDevice[] _snapshot = [];
	private int _nextId;
	private volatile bool _keyboardObserved;
	private volatile bool _mouseObserved;

	public DeviceRegistry(TimeProvider time, TimeSpan retention, RolePriority? priority = null)
	{
		_time = time;
		_retention = retention;
		_priority = priority ?? RolePriority.Default;
	}

	/// <summary>Подключённые сейчас устройства, по возрастанию <see cref="DeviceId"/>.</summary>
	public IReadOnlyList<InputDevice> Snapshot => Volatile.Read(ref _snapshot);

	/// <summary>Есть ли подключённая клавиатура (не агрегат) с рабочим Observe: тогда агрегатные клавиатурные события — дубли.</summary>
	public bool KeyboardObserved => _keyboardObserved;

	/// <summary>Есть ли подключённая мышь (не агрегат) с рабочим Observe: тогда агрегатные события мыши — дубли.</summary>
	public bool MouseObserved => _mouseObserved;

	/// <summary>Возвращает запись, в том числе об устройстве, удалённом недавно.</summary>
	public bool TryGet(DeviceId id, out DeviceEntry entry) => _entries.TryGetValue(id, out entry!);

	public bool TryGetLive(DeviceId id, out DeviceEntry entry) =>
		_entries.TryGetValue(id, out entry!) && entry.Live;

	/// <exception cref="InputException">Ключ уже занят подключённым устройством.</exception>
	public InputDevice Add(ushort backend, string backendId, DeviceDescriptor descriptor) =>
		Add(backend, backendId, descriptor, out _);

	/// <param name="created">
	/// <c>true</c> — появилось новое (или вернулось удалённое) устройство; <c>false</c> — дескриптор присоединён
	/// к уже подключённому устройству другого backend.
	/// </param>
	/// <exception cref="InputException">Ключ уже занят подключённым устройством.</exception>
	public InputDevice Add(ushort backend, string backendId, DeviceDescriptor descriptor, out bool created)
	{
		lock (_gate)
		{
			PurgeExpired();

			var provider = new DeviceProvider(backend, backendId, descriptor);

			if (TryFindMatch(provider, out var match))
			{
				EnsureKeyIsFree(descriptor.Key, match.Device.Id);
				_ids[descriptor.Key] = match.Device.Id;
				match.State = BuildState(match.Device.Id, [.. match.State.Providers, provider]);
				match.Remember(backend);
				created = false;
				RefreshSnapshot();
				return match.Device;
			}

			DeviceEntry entry;
			if (_ids.TryGetValue(descriptor.Key, out var existingId)
				&& _entries.TryGetValue(existingId, out var existing)
				&& (!existing.Live || OwnsKey(existing, descriptor.Key)))
			{
				if (existing.Live)
				{
					throw InputException.DeviceBusy(
						$"Устройство с ключом '{descriptor.Key}' уже зарегистрировано backend '{existing.Device.BackendId}'.");
				}

				entry = new DeviceEntry(BuildState(existingId, [provider]));
			}
			else
			{
				var id = new DeviceId(++_nextId);
				_ids[descriptor.Key] = id;
				entry = new DeviceEntry(BuildState(id, [provider]));
			}

			entry.Remember(backend);
			_entries[entry.Device.Id] = entry;
			created = true;
			RefreshSnapshot();
			return entry.Device;
		}
	}

	/// <summary>
	/// Убирает поставщика <paramref name="backend"/> из устройства. <c>false</c>, если устройства нет,
	/// оно уже удалено или этот backend его не поставляет.
	/// </summary>
	public bool Remove(DeviceId id, ushort backend, out InputDevice device) =>
		Remove(id, backend, out device, out _);

	/// <param name="deviceGone">
	/// <c>true</c> — это был последний поставщик, устройство пропало; <c>false</c> — устройство осталось
	/// (его роли перешли к другим поставщикам).
	/// </param>
	public bool Remove(DeviceId id, ushort backend, out InputDevice device, out bool deviceGone)
	{
		lock (_gate)
		{
			if (_entries.TryGetValue(id, out var entry) && entry.Live && entry.State.HasProvider(backend))
			{
				deviceGone = DropProvider(entry, backend);
				RefreshSnapshot();
				device = entry.Device;
				return true;
			}

			device = null!;
			deviceGone = false;
			return false;
		}
	}

	/// <summary>Убирает backend из всех устройств и возвращает устройства, которые пропали совсем.</summary>
	public IReadOnlyList<InputDevice> RemoveAll(ushort backend) => RemoveAll(backend, null);

	/// <param name="survivors">Сюда попадают устройства, которые остались благодаря другим поставщикам.</param>
	public IReadOnlyList<InputDevice> RemoveAll(ushort backend, List<DeviceId>? survivors)
	{
		lock (_gate)
		{
			List<InputDevice> removed = [];
			var changed = false;
			foreach (var entry in _entries.Values)
			{
				if (!entry.Live || !entry.State.HasProvider(backend))
				{
					continue;
				}

				changed = true;
				if (DropProvider(entry, backend))
				{
					removed.Add(entry.Device);
				}
				else
				{
					survivors?.Add(entry.Device.Id);
				}
			}

			if (changed)
			{
				RefreshSnapshot();
			}

			return removed;
		}
	}

	/// <summary>Убирает поставщика; <c>true</c>, если устройство пропало. У пропавшего устройства список поставщиков сохраняется: через него проходят последние события отключения.</summary>
	private bool DropProvider(DeviceEntry entry, ushort backend)
	{
		var remaining = entry.State.Providers.Where(p => p.Backend != backend).ToArray();
		if (remaining.Length == 0)
		{
			MarkRemoved(entry);
			return true;
		}

		entry.State = BuildState(entry.Device.Id, remaining);
		return false;
	}

	private bool TryFindMatch(DeviceProvider candidate, out DeviceEntry match)
	{
		match = null!;
		if (!IsMatchable(candidate.Descriptor))
		{
			return false;
		}

		foreach (var entry in _entries.Values)
		{
			if (!entry.Live || entry.State.HasProvider(candidate.Backend))
			{
				continue;
			}

			foreach (var provider in entry.State.Providers)
			{
				if (IsSameHardware(provider.Descriptor, candidate.Descriptor))
				{
					match = entry;
					return true;
				}
			}
		}

		return false;
	}

	/// <summary>Сопоставлять можно только устройства с VendorId, ProductId и серийным номером: без серийного номера два одинаковых устройства неразличимы.</summary>
	private static bool IsMatchable(DeviceDescriptor descriptor) =>
		descriptor.Origin != DeviceOrigin.Aggregate
		&& descriptor.Identity is { VendorId: not null, ProductId: not null, SerialNumber: { Length: > 0 } };

	private static bool IsSameHardware(DeviceDescriptor a, DeviceDescriptor b) =>
		IsMatchable(a)
		&& IsMatchable(b)
		&& a.Bus == b.Bus
		&& a.Identity!.VendorId == b.Identity!.VendorId
		&& a.Identity.ProductId == b.Identity.ProductId
		&& string.Equals(a.Identity.SerialNumber, b.Identity.SerialNumber, StringComparison.Ordinal);

	private static bool OwnsKey(DeviceEntry entry, DeviceKey key) =>
		entry.State.Providers.Any(p => p.Descriptor.Key == key);

	private void EnsureKeyIsFree(DeviceKey key, DeviceId owner)
	{
		if (_ids.TryGetValue(key, out var other)
			&& other != owner
			&& _entries.TryGetValue(other, out var entry)
			&& entry.Live
			&& OwnsKey(entry, key))
		{
			throw InputException.DeviceBusy(
				$"Устройство с ключом '{key}' уже зарегистрировано backend '{entry.Device.BackendId}'.");
		}
	}

	private DeviceState BuildState(DeviceId id, DeviceProvider[] providers)
	{
		var kind = providers[0].Descriptor.Kind;

		Dictionary<InputOperation, ushort> owners = new();
		foreach (var operation in RoleOperations)
		{
			var best = Best(providers, kind, operation, requireSupport: true);
			if (best is not null)
			{
				owners[operation] = best.Backend;
			}
		}

		var observeOwner = owners.TryGetValue(InputOperation.Observe, out var declared)
			? declared
			: Best(providers, kind, InputOperation.Observe, requireSupport: false)!.Backend;

		var ordered = providers
			.OrderBy(p => p.Backend == observeOwner ? 0 : 1)
			.ThenBy(p => _priority.Rank(p, kind, InputOperation.Observe))
			.ToArray();

		return new DeviceState(Create(id, ordered, owners), providers, owners, observeOwner);
	}

	private DeviceProvider? Best(DeviceProvider[] providers, InputDeviceKind kind, InputOperation operation, bool requireSupport)
	{
		DeviceProvider? best = null;
		var bestRank = long.MaxValue;
		foreach (var provider in providers)
		{
			if (requireSupport
				&& !(provider.Descriptor.Operations.TryGetValue(operation, out var support) && support.IsSupported))
			{
				continue;
			}

			var rank = _priority.Rank(provider, kind, operation);
			if (best is null || rank < bestRank)
			{
				best = provider;
				bestRank = rank;
			}
		}

		return best;
	}

	/// <summary>Собирает публичное описание: основной поставщик (владелец Observe) задаёт имя и ключ, операции и возможности объединяются.</summary>
	private static InputDevice Create(DeviceId id, DeviceProvider[] ordered, Dictionary<InputOperation, ushort> owners)
	{
		var primary = ordered[0];
		var descriptor = primary.Descriptor;

		Dictionary<InputOperation, OperationSupport> operations = new();
		InputCapabilities capabilities = default;
		foreach (var provider in ordered)
		{
			capabilities |= provider.Descriptor.Capabilities;
			foreach (var pair in provider.Descriptor.Operations)
			{
				if (!operations.TryGetValue(pair.Key, out var current) || (!current.IsSupported && pair.Value.IsSupported))
				{
					operations[pair.Key] = pair.Value;
				}
			}
		}

		Dictionary<InputOperation, string> roleOwners = new();
		foreach (var pair in owners)
		{
			roleOwners[pair.Key] = ordered.First(p => p.Backend == pair.Value).BackendId;
		}

		return new InputDevice
		{
			Id = id,
			Key = descriptor.Key,
			Name = descriptor.Name,
			Kind = descriptor.Kind,
			Origin = descriptor.Origin,
			BackendId = primary.BackendId,
			Bus = descriptor.Bus,
			Identity = descriptor.Identity ?? ordered.Select(p => p.Descriptor.Identity).FirstOrDefault(i => i is not null),
			Capabilities = capabilities,
			Operations = operations,
			Properties = descriptor.Properties,
			Providers = [.. ordered.Select(p => p.BackendId)],
			RoleOwners = roleOwners,
		};
	}

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

		_keyboardObserved = devices.Any(d => ObservesPerDevice(d, InputDeviceKind.Keyboard));
		_mouseObserved = devices.Any(d => ObservesPerDevice(d, InputDeviceKind.Mouse));
	}

	private static bool ObservesPerDevice(InputDevice device, InputDeviceKind kind) =>
		device.Origin != DeviceOrigin.Aggregate
		&& device.Kind == kind
		&& device.Operations.TryGetValue(InputOperation.Observe, out var support)
		&& support.IsSupported;

	private void PurgeExpired()
	{
		var now = _time.GetUtcNow();
		foreach (var entry in _entries.Values)
		{
			if (!entry.Live && now - entry.RemovedAt > _retention)
			{
				var id = entry.Device.Id;
				_entries.TryRemove(id, out _);
				foreach (var key in _ids.Where(pair => pair.Value == id).Select(pair => pair.Key).ToList())
				{
					_ids.Remove(key);
				}
			}
		}
	}
}

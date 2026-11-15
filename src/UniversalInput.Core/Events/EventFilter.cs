namespace UniversalInput.Core.Events;

/// <summary>Фильтр подписки, разобранный из <see cref="SubscriptionOptions"/> в множества для быстрой проверки.</summary>
internal sealed class EventFilter
{
	private readonly HashSet<InputEventKind>? _kinds;
	private readonly HashSet<DeviceId>? _devices;
	private readonly HashSet<InputDeviceKind>? _deviceKinds;
	private readonly HashSet<ushort>? _backends;
	private readonly HashSet<DeviceOrigin>? _origins;
	private readonly Func<InputEvent, bool>? _predicate;

	private EventFilter(SubscriptionOptions options, IReadOnlyDictionary<string, ushort> backendIndexes)
	{
		_kinds = options.Kinds is null ? null : new HashSet<InputEventKind>(options.Kinds);
		_devices = options.Devices is null ? null : new HashSet<DeviceId>(options.Devices);
		_deviceKinds = options.DeviceKinds is null ? null : new HashSet<InputDeviceKind>(options.DeviceKinds);
		_origins = options.Origins is null ? null : new HashSet<DeviceOrigin>(options.Origins);
		_predicate = options.Predicate;

		if (options.BackendIds is not null)
		{
			_backends = new HashSet<ushort>();
			foreach (var id in options.BackendIds)
			{
				if (!backendIndexes.TryGetValue(id, out var index))
				{
					throw new ArgumentException($"Backend '{id}' не зарегистрирован.", nameof(options));
				}

				_backends.Add(index);
			}
		}
	}

	public static EventFilter Create(SubscriptionOptions options, IReadOnlyDictionary<string, ushort> backendIndexes) =>
		new(options, backendIndexes);

	/// <summary>
	/// Проверяет событие. <see cref="InputEventKind.StreamGap"/> проходит всегда (кроме фильтра по backend):
	/// потребитель должен видеть пропуск. Исключение предиката считается отказом и не роняет диспетчер.
	/// </summary>
	public bool Matches(in InputEvent inputEvent, InputDevice? device)
	{
		if (_backends is not null && !_backends.Contains(inputEvent.Backend))
		{
			return false;
		}

		if (inputEvent.Kind == InputEventKind.StreamGap)
		{
			return true;
		}

		if (_kinds is not null && !_kinds.Contains(inputEvent.Kind))
		{
			return false;
		}

		if (_devices is not null && !_devices.Contains(inputEvent.Device))
		{
			return false;
		}

		if (_deviceKinds is not null && (device is null || !_deviceKinds.Contains(device.Kind)))
		{
			return false;
		}

		if (_origins is not null && (device is null || !_origins.Contains(device.Origin)))
		{
			return false;
		}

		if (_predicate is null)
		{
			return true;
		}

		try
		{
			return _predicate(inputEvent);
		}
		catch (Exception)
		{
			return false;
		}
	}
}

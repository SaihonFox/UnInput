namespace UniversalInput.Core.Devices;

/// <summary>
/// Неизменяемый срез состояния логического устройства: поставщики и владельцы ролей.
/// Реестр заменяет его целиком, поэтому читатели без блокировки всегда видят согласованные данные.
/// </summary>
internal sealed class DeviceState
{
	public DeviceState(
		InputDevice device,
		DeviceProvider[] providers,
		IReadOnlyDictionary<InputOperation, ushort> owners,
		ushort observeOwner)
	{
		Device = device;
		Providers = providers;
		Owners = owners;
		ObserveOwner = observeOwner;
	}

	public InputDevice Device { get; }

	/// <summary>Поставщики в порядке подключения.</summary>
	public DeviceProvider[] Providers { get; }

	/// <summary>Владельцы ролей; роли, которую никто не поддерживает, здесь нет.</summary>
	public IReadOnlyDictionary<InputOperation, ushort> Owners { get; }

	/// <summary>
	/// Backend, чьи события доходят до подписчиков. Если ни один поставщик не объявил Observe,
	/// это лучший по приоритету поставщик: устройство с единственным поставщиком не должно «замолчать».
	/// </summary>
	public ushort ObserveOwner { get; }

	public bool HasProvider(ushort backend)
	{
		foreach (var provider in Providers)
		{
			if (provider.Backend == backend)
			{
				return true;
			}
		}

		return false;
	}

	public bool TryGetOwner(InputOperation operation, out ushort backend) => Owners.TryGetValue(operation, out backend);
}

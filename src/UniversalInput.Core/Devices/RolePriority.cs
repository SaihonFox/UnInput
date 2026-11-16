using UniversalInput.Core.Configuration;

namespace UniversalInput.Core.Devices;

/// <summary>
/// Порядок выбора владельца роли среди backend, видящих одно устройство.
/// Сначала список по виду устройства (для DirectAccess — список сырых reports), затем общий
/// <see cref="InputHubOptions.BackendPriority"/>, затем порядок регистрации. Меньшее число — выше приоритет.
/// </summary>
internal sealed class RolePriority
{
	private const int Absent = 1000;

	private readonly Dictionary<string, int> _global;
	private readonly Dictionary<string, int> _raw;
	private readonly Dictionary<string, Dictionary<string, int>> _byKind = new(StringComparer.Ordinal);

	public RolePriority(InputHubOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		_global = Index(options.BackendPriority);
		_raw = Index(options.RawAccessPriority);
		foreach (var pair in options.KindPriority)
		{
			_byKind[pair.Key] = Index(pair.Value);
		}
	}

	public static RolePriority Default { get; } = new(new InputHubOptions());

	public long Rank(DeviceProvider provider, InputDeviceKind kind, InputOperation operation)
	{
		long specific = Absent;
		if (operation == InputOperation.DirectAccess)
		{
			specific = Position(_raw, provider.BackendId);
		}
		else if (kind.Name is not null && _byKind.TryGetValue(kind.Name, out var list))
		{
			specific = Position(list, provider.BackendId);
		}

		return (specific << 40) | ((long)Position(_global, provider.BackendId) << 20) | provider.Backend;
	}

	private static int Position(Dictionary<string, int> positions, string id) =>
		positions.TryGetValue(id, out var position) ? position : Absent;

	private static Dictionary<string, int> Index(IEnumerable<string> ids)
	{
		Dictionary<string, int> positions = new(StringComparer.Ordinal);
		var position = 0;
		foreach (var id in ids)
		{
			positions.TryAdd(id, position++);
		}

		return positions;
	}
}

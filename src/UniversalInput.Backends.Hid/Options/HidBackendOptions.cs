using UniversalInput.Backends.Hid.Discovery;

namespace UniversalInput.Backends.Hid.Options;

public sealed class HidBackendOptions
{
	/// <summary>
	/// Безопасный период пересчёта списка устройств. Основной сигнал — событие HidSharp о смене списка,
	/// но на разных ОС оно срабатывает не одинаково надёжно, поэтому список ещё и периодически сверяется.
	/// </summary>
	public TimeSpan RescanInterval { get; set; } = TimeSpan.FromSeconds(10);

	/// <summary>Какие HID-интерфейсы показывать. Без фильтра — все, которые видит ОС, включая те, которые приложению не открыть.</summary>
	public Func<HidDeviceInfo, bool>? Filter { get; set; }

	public void Validate()
	{
		if (RescanInterval <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(RescanInterval), RescanInterval, "Период пересчёта должен быть положительным.");
		}
	}
}

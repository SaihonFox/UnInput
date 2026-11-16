using UniversalInput.Backends.Hid.Options;

namespace UniversalInput.Backends.Hid.Extensions;

public static class HidBackendBuilderExtensions
{
	/// <summary>Подключает backend HID-устройств (<see cref="HidBackend"/>).</summary>
	/// <param name="builder">Построитель UniversalInput.</param>
	/// <param name="configure">Фильтр устройств и период пересчёта; без неё используются значения по умолчанию.</param>
	/// <param name="required">Обязателен ли backend в режиме FailFast.</param>
	public static IInputBackendBuilder AddHid(
		this IInputBackendBuilder builder, Action<HidBackendOptions>? configure = null, bool required = false)
	{
		ArgumentNullException.ThrowIfNull(builder);

		return builder.AddBackend(
			_ =>
			{
				var options = new HidBackendOptions();
				configure?.Invoke(options);
				return new HidBackend(options);
			},
			required);
	}
}

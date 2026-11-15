using UniversalInput.Backends.Serial.Options;

namespace UniversalInput.Backends.Serial.Extensions;

public static class SerialBackendBuilderExtensions
{
	/// <summary>Подключает backend последовательных портов (<see cref="SerialBackend"/>).</summary>
	/// <param name="builder">Построитель UniversalInput.</param>
	/// <param name="configure">Настройка опроса и параметров порта; без неё используются значения по умолчанию.</param>
	/// <param name="required">Обязателен ли backend в режиме FailFast.</param>
	public static IInputBackendBuilder AddSerial(
		this IInputBackendBuilder builder, Action<SerialBackendOptions>? configure = null, bool required = false)
	{
		ArgumentNullException.ThrowIfNull(builder);

		return builder.AddBackend(
			_ =>
			{
				var options = new SerialBackendOptions();
				configure?.Invoke(options);
				return new SerialBackend(options);
			},
			required);
	}
}

using UniversalInput.Backends.Sdl.Options;

namespace UniversalInput.Backends.Sdl.Extensions;

public static class SdlBackendBuilderExtensions
{
	/// <summary>Подключает backend геймпадов на SDL3 (<see cref="SdlBackend"/>). Нативную библиотеку SDL3 приложение поставляет само.</summary>
	/// <param name="builder">Построитель UniversalInput.</param>
	/// <param name="configure">Путь к библиотеке, датчики, фоновые события; без неё — значения по умолчанию.</param>
	/// <param name="required">Обязателен ли backend в режиме FailFast.</param>
	public static IInputBackendBuilder AddSdl(
		this IInputBackendBuilder builder, Action<SdlBackendOptions>? configure = null, bool required = false)
	{
		ArgumentNullException.ThrowIfNull(builder);

		return builder.AddBackend(
			_ =>
			{
				var options = new SdlBackendOptions();
				configure?.Invoke(options);
				return new SdlBackend(options);
			},
			required);
	}
}

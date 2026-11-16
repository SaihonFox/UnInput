namespace UniversalInput.Backends.GlobalHook.Extensions;

public static class GlobalHookBackendBuilderExtensions
{
	/// <summary>Подключает наблюдение за системным вводом через глобальный хук (<see cref="GlobalHookBackend"/>).</summary>
	/// <param name="builder">Построитель UniversalInput.</param>
	/// <param name="required">Обязателен ли backend в режиме FailFast.</param>
	public static IInputBackendBuilder AddGlobalHook(this IInputBackendBuilder builder, bool required = false)
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.AddBackend(_ => new GlobalHookBackend(), required);
	}
}

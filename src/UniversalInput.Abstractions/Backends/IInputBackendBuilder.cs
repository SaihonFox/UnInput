namespace UniversalInput.Abstractions.Backends;

/// <summary>
/// Минимальная точка подключения backend. Пакеты backend добавляют к ней методы расширения вида
/// <c>AddSerial(this IInputBackendBuilder builder)</c> и зависят только от Abstractions.
/// Реализует построитель из <c>UniversalInput.Hosting</c>.
/// </summary>
public interface IInputBackendBuilder
{
	/// <summary>Регистрирует backend, созданный фабрикой. <paramref name="required"/> важен в режиме FailFast.</summary>
	IInputBackendBuilder AddBackend(Func<IServiceProvider, IInputBackend> factory, bool required = false);
}

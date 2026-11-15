using Microsoft.Extensions.DependencyInjection;
using UniversalInput.Core.Configuration;

namespace UniversalInput.Hosting.Builder;

/// <summary>
/// Построитель UniversalInput. Пакеты backend добавляют методы расширения вида
/// <c>AddMyBackend(this IInputBuilder builder)</c>; Core о них ничего не знает.
/// </summary>
public interface IInputBuilder
{
	IServiceCollection Services { get; }

	/// <summary>Регистрирует backend, созданный контейнером. <paramref name="required"/> важен в режиме FailFast.</summary>
	IInputBuilder AddBackend<TBackend>(bool required = false)
		where TBackend : class, IInputBackend;

	/// <summary>Регистрирует backend, созданный фабрикой.</summary>
	IInputBuilder AddBackend(Func<IServiceProvider, IInputBackend> factory, bool required = false);

	IInputBuilder Configure(Action<InputHubOptions> configure);
}

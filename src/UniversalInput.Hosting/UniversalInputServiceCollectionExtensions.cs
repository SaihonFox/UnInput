using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UniversalInput.Core.Backends;
using UniversalInput.Core.Configuration;
using UniversalInput.Core.Hub;
using UniversalInput.Hosting.Builder;
using UniversalInput.Hosting.Services;

namespace UniversalInput.Hosting;

public static class UniversalInputServiceCollectionExtensions
{
	/// <summary>
	/// Добавляет UniversalInput: <see cref="IInputHub"/> и фоновую службу, которая его запускает.
	/// Backend подключаются через <paramref name="configure"/>. Повторный вызов дополняет настройки.
	/// </summary>
	public static IServiceCollection AddUniversalInput(
		this IServiceCollection services, Action<IInputBuilder>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddOptions<InputHubOptions>();
		services.TryAddSingleton(TimeProvider.System);

		if (!services.Any(descriptor => descriptor.ServiceType == typeof(InputHub)))
		{
			services.AddSingleton(provider => new InputHub(
				provider.GetServices<BackendRegistration>(),
				provider.GetRequiredService<IOptions<InputHubOptions>>().Value,
				provider.GetRequiredService<TimeProvider>(),
				provider.GetService<ILogger<InputHub>>()));
			services.AddSingleton<IInputHub>(provider => provider.GetRequiredService<InputHub>());
			services.AddHostedService<InputHostedService>();
		}

		configure?.Invoke(new InputBuilder(services));
		return services;
	}
}

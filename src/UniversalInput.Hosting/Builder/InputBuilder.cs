using Microsoft.Extensions.DependencyInjection;
using UniversalInput.Core.Backends;
using UniversalInput.Core.Configuration;

namespace UniversalInput.Hosting.Builder;

internal sealed class InputBuilder : IInputBuilder
{
	public InputBuilder(IServiceCollection services) => Services = services;

	public IServiceCollection Services { get; }

	public IInputBuilder AddBackend<TBackend>(bool required = false)
		where TBackend : class, IInputBackend
	{
		Services.AddSingleton<TBackend>();
		return AddBackend(provider => provider.GetRequiredService<TBackend>(), required);
	}

	public IInputBuilder AddBackend(Func<IServiceProvider, IInputBackend> factory, bool required = false)
	{
		ArgumentNullException.ThrowIfNull(factory);
		Services.AddSingleton(provider => new BackendRegistration(factory(provider), required));
		return this;
	}

	public IInputBuilder Configure(Action<InputHubOptions> configure)
	{
		ArgumentNullException.ThrowIfNull(configure);
		Services.Configure(configure);
		return this;
	}
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UniversalInput.Core.Configuration;
using UniversalInput.Core.Hub;
using UniversalInput.Hosting.Builder;
using UniversalInput.Testing.Mock;

namespace UniversalInput.Hosting.Tests;

public class AddUniversalInputTests
{
	private static MockInputBackend WithKeyboard(string id = "mock")
	{
		var backend = new MockInputBackend(id);
		backend.InitialDevices.Add(MockInputBackend.Describe("kbd", InputDeviceKind.Keyboard));
		return backend;
	}

	[Fact]
	public async Task AddUniversalInput_RegistersOneHubForBothServiceTypes()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddUniversalInput();
		await using var provider = services.BuildServiceProvider();

		var byInterface = provider.GetRequiredService<IInputHub>();
		var byClass = provider.GetRequiredService<InputHub>();

		Assert.Same(byClass, byInterface);
		Assert.Same(byInterface, provider.GetRequiredService<IInputHub>());
	}

	[Fact]
	public async Task RepeatedCalls_AddOneHostedService_AndCollectAllBackends()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddUniversalInput(input => input.AddBackend(_ => new MockInputBackend("a")));
		services.AddUniversalInput(input => input.AddBackend(_ => new MockInputBackend("b")));
		await using var provider = services.BuildServiceProvider();

		Assert.Single(provider.GetServices<IHostedService>());
		var hub = provider.GetRequiredService<IInputHub>();
		await provider.GetRequiredService<InputHub>().StartAsync();
		Assert.Equal<string>(["a", "b"], hub.Backends.Select(b => b.Id));
	}

	[Fact]
	public async Task AddBackendByType_ResolvesItFromTheContainer()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddUniversalInput(input => input.AddBackend<MockInputBackend>());
		await using var provider = services.BuildServiceProvider();
		var backend = provider.GetRequiredService<MockInputBackend>();

		var hub = provider.GetRequiredService<InputHub>();
		await hub.StartAsync();

		Assert.Equal("mock", Assert.Single(hub.Backends).Id);
		Assert.Equal(1, backend.StartCount);
	}

	[Fact]
	public async Task Configure_AppliesOptions()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddUniversalInput(input => input.Configure(o =>
		{
			o.BackendQueueCapacity = 16;
			o.FailFast = true;
			o.BackendPriority = ["b", "a"];
		}));
		await using var provider = services.BuildServiceProvider();

		var options = provider.GetRequiredService<IOptions<InputHubOptions>>().Value;

		Assert.Equal(16, options.BackendQueueCapacity);
		Assert.True(options.FailFast);
		Assert.Equal<string>(["b", "a"], options.BackendPriority);
	}

	[Fact]
	public async Task Host_StartsTheHubWithTheHost_AndStopsItOnShutdown()
	{
		var backend = WithKeyboard();
		var builder = Host.CreateApplicationBuilder();
		builder.Logging.ClearProviders();
		builder.Services.AddUniversalInput(input => input.AddMock(backend));
		using var host = builder.Build();

		await host.StartAsync();
		var hub = host.Services.GetRequiredService<IInputHub>();

		Assert.Equal(BackendState.Running, Assert.Single(hub.Backends).State);
		Assert.Equal("kbd", Assert.Single(hub.Devices).Name);

		await host.StopAsync();

		Assert.Equal(BackendState.Stopped, Assert.Single(hub.Backends).State);
		Assert.Equal(1, backend.StopCount);
	}

	[Fact]
	public async Task Host_WithFailFast_DoesNotStartWhenARequiredBackendIsUnavailable()
	{
		var backend = new MockInputBackend("needed")
		{
			ProbeResult = BackendProbe.Unavailable(InputErrorCode.PermissionDenied, "нет прав", "выдайте права"),
		};
		var builder = Host.CreateApplicationBuilder();
		builder.Logging.ClearProviders();
		builder.Services.AddUniversalInput(input => input
			.AddBackend(_ => backend, required: true)
			.Configure(o => o.FailFast = true));
		using var host = builder.Build();

		var error = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());

		var inputError = error as InputException
			?? (error as AggregateException)?.InnerExceptions.OfType<InputException>().FirstOrDefault();
		Assert.NotNull(inputError);
		Assert.Equal(InputErrorCode.PermissionDenied, inputError.Code);
	}

	[Fact]
	public async Task Host_WithoutFailFast_StartsEvenIfABackendIsUnavailable()
	{
		var backend = new MockInputBackend("optional")
		{
			ProbeResult = BackendProbe.Unavailable(InputErrorCode.BackendUnavailable, "нет библиотеки"),
		};
		var builder = Host.CreateApplicationBuilder();
		builder.Logging.ClearProviders();
		builder.Services.AddUniversalInput(input => input.AddMock(backend));
		using var host = builder.Build();

		await host.StartAsync();

		var hub = host.Services.GetRequiredService<IInputHub>();
		Assert.Equal(BackendState.Unavailable, Assert.Single(hub.Backends).State);
		await host.StopAsync();
	}
}

/// <summary>Так выглядит расширение стороннего backend: пакет добавляет метод к <see cref="IInputBuilder"/>.</summary>
internal static class MockBuilderExtensions
{
	public static IInputBuilder AddMock(this IInputBuilder builder, MockInputBackend backend) =>
		builder.AddBackend(_ => backend);
}

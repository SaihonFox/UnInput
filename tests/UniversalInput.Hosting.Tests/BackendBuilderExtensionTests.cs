using Microsoft.Extensions.DependencyInjection;
using UniversalInput.Core.Hub;
using UniversalInput.Testing.Mock;

namespace UniversalInput.Hosting.Tests;

/// <summary>Метод расширения, как у пакета backend: знает только <see cref="IInputBackendBuilder"/>.</summary>
internal static class FakeBackendPackageExtensions
{
	public static IInputBackendBuilder AddFakePackage(this IInputBackendBuilder builder, bool required = false) =>
		builder.AddBackend(_ => new MockInputBackend("fake-package"), required);
}

public class BackendBuilderExtensionTests
{
	[Fact]
	public async Task ExtensionOnTheMinimalBuilder_WorksThroughAddUniversalInput()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddUniversalInput(input => input.AddFakePackage());
		await using var provider = services.BuildServiceProvider();

		var hub = provider.GetRequiredService<InputHub>();
		await hub.StartAsync(TestContext.Current.CancellationToken);

		Assert.Equal("fake-package", Assert.Single(hub.Backends).Id);
	}
}

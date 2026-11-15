using Microsoft.Extensions.Hosting;
using UniversalInput.Core.Hub;

namespace UniversalInput.Hosting.Services;

/// <summary>Запускает и останавливает <see cref="InputHub"/> вместе с хостом (консоль, Worker Service, Desktop).</summary>
internal sealed class InputHostedService : IHostedService
{
	private readonly InputHub _hub;

	public InputHostedService(InputHub hub) => _hub = hub;

	public Task StartAsync(CancellationToken cancellationToken) => _hub.StartAsync(cancellationToken);

	public Task StopAsync(CancellationToken cancellationToken) => _hub.StopAsync(cancellationToken);
}

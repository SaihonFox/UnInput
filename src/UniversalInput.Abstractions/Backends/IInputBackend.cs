using System.Diagnostics.CodeAnalysis;

namespace UniversalInput.Abstractions.Backends;

/// <summary>
/// Источник устройств и событий. Реализует только то, что умеет; остальное — через <see cref="IBackendFeature"/>.
/// </summary>
/// <remarks>
/// <see cref="StopAsync"/> может вызываться после неудачного <see cref="StartAsync"/> и повторно, а
/// <see cref="IAsyncDisposable.DisposeAsync"/> — повторно (его вызывают и Core, и контейнер DI): обе операции должны быть идемпотентными.
/// </remarks>
public interface IInputBackend : IAsyncDisposable
{
	/// <summary>Уникальный идентификатор backend, например <c>"mock"</c> или <c>"hid"</c>.</summary>
	string Id { get; }

	ValueTask<BackendProbe> ProbeAsync(CancellationToken cancellationToken);

	/// <summary>Подписывается на источники и перечисляет устройства через <see cref="IBackendContext.AddDevice"/>.</summary>
	ValueTask StartAsync(IBackendContext context, CancellationToken cancellationToken);

	ValueTask StopAsync(CancellationToken cancellationToken);

	bool TryGetFeature<TFeature>([NotNullWhen(true)] out TFeature? feature)
		where TFeature : class, IBackendFeature;
}

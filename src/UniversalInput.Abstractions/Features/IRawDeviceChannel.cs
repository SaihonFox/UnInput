namespace UniversalInput.Abstractions.Features;

public interface IRawDeviceChannel : IAsyncDisposable
{
	/// <summary>Читает один report; возвращает число записанных байт.</summary>
	ValueTask<int> ReadAsync(Memory<byte> report, CancellationToken cancellationToken);

	ValueTask WriteAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken);
}

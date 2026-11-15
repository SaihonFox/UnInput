using System.IO.Ports;

namespace UniversalInput.Backends.Serial.Ports;

internal sealed class SystemSerialConnection : ISerialConnection
{
	private readonly SerialPort _port;
	private readonly Stream _stream;

	public SystemSerialConnection(SerialPort port)
	{
		_port = port;
		_stream = port.BaseStream;
	}

	public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
		_stream.ReadAsync(buffer, cancellationToken);

	public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken) =>
		_stream.WriteAsync(data, cancellationToken);

	/// <summary>Закрытие порта может ждать завершения чтения, поэтому выполняется на пуле потоков.</summary>
	public async ValueTask DisposeAsync()
	{
		await Task.Run(_port.Dispose).ConfigureAwait(false);
	}
}

namespace UniversalInput.Backends.Serial.Ports;

/// <summary>Открытое соединение с портом. Выделено в интерфейс, чтобы backend можно было проверять без настоящих портов.</summary>
internal interface ISerialConnection : IAsyncDisposable
{
	ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

	ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
}

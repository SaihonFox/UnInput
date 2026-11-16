namespace UniversalInput.Backends.Hid.Platform;

/// <summary>Открытое HID-устройство. Первый байт отчёта — его идентификатор (0, если устройство нумерацию не использует).</summary>
internal interface IHidConnection : IAsyncDisposable
{
	ValueTask<int> ReadAsync(Memory<byte> report, CancellationToken cancellationToken);

	ValueTask WriteAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken);
}

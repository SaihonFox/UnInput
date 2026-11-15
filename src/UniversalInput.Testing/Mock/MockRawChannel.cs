using System.Collections.Concurrent;
using System.Threading.Channels;

namespace UniversalInput.Testing.Mock;

/// <summary>Канал прямого доступа для тестов: входящие reports задаёт тест, исходящие записываются.</summary>
public sealed class MockRawChannel : IRawDeviceChannel
{
	private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();

	public MockRawChannel(DeviceId device, RawAccessMode mode)
	{
		Device = device;
		Mode = mode;
	}

	public DeviceId Device { get; }

	public RawAccessMode Mode { get; }

	public ConcurrentQueue<byte[]> Written { get; } = new();

	public bool IsDisposed { get; private set; }

	/// <summary>Ставит report в очередь чтения.</summary>
	public void Push(params byte[] report) => _incoming.Writer.TryWrite(report);

	/// <summary>Имитирует сбой устройства: ожидающее и последующие чтения завершатся ошибкой.</summary>
	public void Break(Exception error) => _incoming.Writer.TryComplete(error);

	public async ValueTask<int> ReadAsync(Memory<byte> report, CancellationToken cancellationToken)
	{
		var data = await _incoming.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
		var length = Math.Min(data.Length, report.Length);
		data.AsMemory(0, length).CopyTo(report);
		return length;
	}

	public ValueTask WriteAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
	{
		Written.Enqueue(report.ToArray());
		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		IsDisposed = true;
		_incoming.Writer.TryComplete();
		return ValueTask.CompletedTask;
	}
}

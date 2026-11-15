using System.Collections.Concurrent;
using System.Threading.Channels;
using UniversalInput.Backends.Serial.Ports;

namespace UniversalInput.Backends.Serial.Tests.Fakes;

internal sealed class FakeSerialConnection : ISerialConnection
{
	private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();

	public ConcurrentQueue<byte[]> Written { get; } = new();

	public bool IsDisposed { get; private set; }

	public void Push(params byte[] data) => _incoming.Writer.TryWrite(data);

	/// <summary>Имитирует вытаскивание адаптера: чтение завершится <see cref="IOException"/>.</summary>
	public void Unplug() => _incoming.Writer.TryComplete(new IOException("Порт отключён."));

	public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		try
		{
			var data = await _incoming.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
			var length = Math.Min(data.Length, buffer.Length);
			data.AsMemory(0, length).CopyTo(buffer);
			return length;
		}
		catch (ChannelClosedException ex)
		{
			throw new IOException("Порт закрыт.", ex.InnerException ?? ex);
		}
	}

	public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		Written.Enqueue(data.ToArray());
		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		IsDisposed = true;
		_incoming.Writer.TryComplete();
		return ValueTask.CompletedTask;
	}
}

using System.Threading.Channels;
using UniversalInput.Backends.Hid.Platform;

namespace UniversalInput.Backends.Hid.Tests.Fakes;

internal sealed class FakeHidConnection : IHidConnection
{
	private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
	private readonly List<byte[]> _written = [];

	public IReadOnlyList<byte[]> Written
	{
		get
		{
			lock (_written)
			{
				return _written.ToArray();
			}
		}
	}

	public bool IsDisposed { get; private set; }

	public void Push(params byte[] report) => _incoming.Writer.TryWrite(report);

	/// <summary>Имитирует отключение: все чтения (в том числе ожидающие) завершаются этой ошибкой.</summary>
	public void Fail(Exception error) => _incoming.Writer.TryComplete(error);

	public async ValueTask<int> ReadAsync(Memory<byte> report, CancellationToken cancellationToken)
	{
		var data = await _incoming.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
		data.CopyTo(report);
		return data.Length;
	}

	public ValueTask WriteAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
	{
		lock (_written)
		{
			_written.Add(report.ToArray());
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		IsDisposed = true;
		return ValueTask.CompletedTask;
	}
}

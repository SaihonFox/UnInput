using System.Buffers;
using HidSharp;

namespace UniversalInput.Backends.Hid.Platform;

/// <summary>
/// HidSharp читает блокирующе, и прервать чтение можно только закрытием потока. Поэтому чтение идёт короткими
/// отрезками: между ними проверяется отмена. Отмена срабатывает с задержкой до одного отрезка.
/// </summary>
internal sealed class HidSharpConnection : IHidConnection
{
	private const int ReadSliceMilliseconds = 250;

	private readonly HidStream _stream;

	public HidSharpConnection(HidStream stream)
	{
		_stream = stream;
		_stream.ReadTimeout = ReadSliceMilliseconds;
	}

	public ValueTask<int> ReadAsync(Memory<byte> report, CancellationToken cancellationToken) =>
		new(Task.Run(() => ReadBlocking(report, cancellationToken), cancellationToken));

	public ValueTask WriteAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken) =>
		new(Task.Run(() => WriteBlocking(report), cancellationToken));

	public ValueTask DisposeAsync()
	{
		_stream.Dispose();
		return ValueTask.CompletedTask;
	}

	private int ReadBlocking(Memory<byte> report, CancellationToken cancellationToken)
	{
		var buffer = ArrayPool<byte>.Shared.Rent(report.Length);
		try
		{
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var length = _stream.Read(buffer, 0, report.Length);
					buffer.AsSpan(0, length).CopyTo(report.Span);
					return length;
				}
				catch (TimeoutException)
				{
					// Отрезок вышел, отчёта не было: проверим отмену и продолжим.
				}
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private void WriteBlocking(ReadOnlyMemory<byte> report)
	{
		var buffer = report.ToArray();
		_stream.Write(buffer, 0, buffer.Length);
	}
}

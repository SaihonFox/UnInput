using System.Threading.Channels;

namespace UniversalInput.Core.Backends;

/// <summary>Ограниченная очередь одного backend. Писать можно из любого потока, читает один диспетчер.</summary>
internal sealed class BackendQueue
{
	private readonly Channel<InputEvent> _channel;
	private long _pendingDrops;

	public BackendQueue(ushort index, string backendId, int capacity)
	{
		Index = index;
		BackendId = backendId;
		_channel = Channel.CreateBounded<InputEvent>(new BoundedChannelOptions(capacity)
		{
			SingleReader = true,
			SingleWriter = false,
			FullMode = BoundedChannelFullMode.Wait,
		});
	}

	public ushort Index { get; }

	public string BackendId { get; }

	public int Count => _channel.Reader.Count;

	/// <summary>Не блокирует. При переполнении событие теряется, а потеря учитывается для StreamGap.</summary>
	public bool TryWrite(in InputEvent inputEvent)
	{
		if (_channel.Writer.TryWrite(inputEvent))
		{
			return true;
		}

		Interlocked.Increment(ref _pendingDrops);
		return false;
	}

	public bool TryRead(out InputEvent inputEvent) => _channel.Reader.TryRead(out inputEvent);

	/// <summary>Забирает число потерянных событий, о которых потребитель ещё не уведомлён.</summary>
	public long TakeDrops() => Interlocked.Exchange(ref _pendingDrops, 0);
}

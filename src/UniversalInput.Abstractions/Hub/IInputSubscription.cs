using System.Threading.Channels;

namespace UniversalInput.Abstractions.Hub;

/// <summary>Подписка на события. Читатель канала один.</summary>
public interface IInputSubscription : IAsyncDisposable
{
	ChannelReader<InputEvent> Reader { get; }

	/// <summary>Сколько событий потеряно этой подпиской.</summary>
	long DroppedCount { get; }
}

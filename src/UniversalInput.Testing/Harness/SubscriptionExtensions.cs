namespace UniversalInput.Testing.Harness;

public static class SubscriptionExtensions
{
	/// <summary>Читает ровно <paramref name="count"/> событий или бросает <see cref="TimeoutException"/>.</summary>
	public static async Task<List<InputEvent>> ReadEventsAsync(
		this IInputSubscription subscription, int count, TimeSpan? timeout = null)
	{
		using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
		List<InputEvent> events = [];
		try
		{
			while (events.Count < count)
			{
				events.Add(await subscription.Reader.ReadAsync(cts.Token).ConfigureAwait(false));
			}
		}
		catch (OperationCanceledException) when (cts.IsCancellationRequested)
		{
			throw new TimeoutException($"Получено {events.Count} из {count} событий.");
		}

		return events;
	}

	/// <summary>Читает события до первого, подходящего под условие (включая его).</summary>
	public static async Task<List<InputEvent>> ReadUntilAsync(
		this IInputSubscription subscription, Func<InputEvent, bool> predicate, TimeSpan? timeout = null)
	{
		using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
		List<InputEvent> events = [];
		try
		{
			while (true)
			{
				var inputEvent = await subscription.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
				events.Add(inputEvent);
				if (predicate(inputEvent))
				{
					return events;
				}
			}
		}
		catch (OperationCanceledException) when (cts.IsCancellationRequested)
		{
			throw new TimeoutException($"Нужное событие не получено; прочитано {events.Count}.");
		}
	}
}

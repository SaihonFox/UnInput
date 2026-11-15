namespace UniversalInput.Testing.Harness;

/// <summary>Ожидание условия, которое выполнится в другом потоке (перезапуск backend, доставка событий).</summary>
public static class Wait
{
	/// <exception cref="TimeoutException">Условие не выполнилось за отведённое время.</exception>
	public static async Task UntilAsync(Func<bool> condition, TimeSpan? timeout = null, string? description = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				throw new TimeoutException($"Условие не выполнилось вовремя{(description is null ? "." : ": " + description)}");
			}

			await Task.Delay(5).ConfigureAwait(false);
		}
	}
}

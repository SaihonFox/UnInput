namespace UniversalInput.Testing.Harness;

/// <summary>Время, которым управляет тест. Таймеры не поддерживаются: нужен только <see cref="GetUtcNow"/>.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
	private DateTimeOffset _now;

	public ManualTimeProvider(DateTimeOffset? start = null)
	{
		_now = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
	}

	public override DateTimeOffset GetUtcNow() => _now;

	public void Advance(TimeSpan delta) => _now += delta;
}

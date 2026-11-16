namespace UniversalInput.Testing.Harness;

/// <summary>
/// Время, которым управляет тест: стоит на месте, пока его не сдвинут <see cref="Advance"/>.
/// Поддерживает таймеры (<see cref="CreateTimer"/>) и метки времени (<see cref="GetTimestamp"/>, такт — 100 нс).
/// Таймеры срабатывают внутри <see cref="Advance"/>, в потоке вызывающего.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
	private readonly object _gate = new();
	private readonly List<ManualTimer> _timers = [];
	private DateTimeOffset _now;

	public ManualTimeProvider(DateTimeOffset? start = null)
		: base(TimeSpan.TicksPerSecond)
	{
		_now = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
	}

	public override DateTimeOffset GetUtcNow()
	{
		lock (_gate)
		{
			return _now;
		}
	}

	public override long GetTimestamp()
	{
		lock (_gate)
		{
			return _now.UtcTicks;
		}
	}

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		ArgumentNullException.ThrowIfNull(callback);

		var timer = new ManualTimer(this, callback, state);
		timer.Change(dueTime, period);
		return timer;
	}

	/// <summary>Сдвигает время вперёд, по пути срабатывают созревшие таймеры (каждый — в момент своего срока).</summary>
	public void Advance(TimeSpan delta)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);

		DateTimeOffset target;
		lock (_gate)
		{
			target = _now + delta;
		}

		while (true)
		{
			ManualTimer? next = null;
			lock (_gate)
			{
				foreach (var timer in _timers)
				{
					if (timer.Due is { } due && due <= target && (next is null || due < next.Due))
					{
						next = timer;
					}
				}

				if (next is null)
				{
					_now = target;
					return;
				}

				if (next.Due!.Value > _now)
				{
					_now = next.Due.Value;
				}

				next.MarkFired();
			}

			next.Invoke();
		}
	}

	private sealed class ManualTimer : ITimer
	{
		private readonly ManualTimeProvider _owner;
		private readonly TimerCallback _callback;
		private readonly object? _state;
		private TimeSpan _period = Timeout.InfiniteTimeSpan;
		private bool _disposed;

		public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
		{
			_owner = owner;
			_callback = callback;
			_state = state;
		}

		/// <summary>Когда сработает в следующий раз; <c>null</c> — таймер остановлен. Читается и меняется под блокировкой провайдера.</summary>
		public DateTimeOffset? Due { get; private set; }

		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			lock (_owner._gate)
			{
				if (_disposed)
				{
					return false;
				}

				if (!_owner._timers.Contains(this))
				{
					_owner._timers.Add(this);
				}

				_period = period;
				Due = dueTime == Timeout.InfiniteTimeSpan ? null : _owner._now + dueTime;
				return true;
			}
		}

		/// <summary>Вызывается под блокировкой провайдера перед обратным вызовом: назначает следующий срок или останавливает таймер.</summary>
		public void MarkFired()
		{
			Due = _period == Timeout.InfiniteTimeSpan || _period <= TimeSpan.Zero ? null : Due + _period;
		}

		public void Invoke() => _callback(_state);

		public void Dispose()
		{
			lock (_owner._gate)
			{
				_disposed = true;
				Due = null;
				_owner._timers.Remove(this);
			}
		}

		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}
	}
}

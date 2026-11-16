using UniversalInput.Testing.Harness;

namespace UniversalInput.Core.Tests.Harness;

public class ManualTimeProviderTests
{
	[Fact]
	public void Advance_MovesUtcNowAndTimestamps()
	{
		var time = new ManualTimeProvider();
		var start = time.GetUtcNow();
		var timestamp = time.GetTimestamp();

		time.Advance(TimeSpan.FromSeconds(3));

		Assert.Equal(start + TimeSpan.FromSeconds(3), time.GetUtcNow());
		Assert.Equal(TimeSpan.FromSeconds(3), time.GetElapsedTime(timestamp, time.GetTimestamp()));
	}

	[Fact]
	public void ATimer_FiresOnceWhenItsDueTimeIsReached()
	{
		var time = new ManualTimeProvider();
		var fired = 0;
		using var timer = time.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

		time.Advance(TimeSpan.FromMilliseconds(999));
		Assert.Equal(0, fired);
		time.Advance(TimeSpan.FromMilliseconds(1));
		time.Advance(TimeSpan.FromSeconds(10));

		Assert.Equal(1, fired);
	}

	[Fact]
	public void APeriodicTimer_FiresOncePerPeriod()
	{
		var time = new ManualTimeProvider();
		var fired = 0;
		using var timer = time.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

		time.Advance(TimeSpan.FromSeconds(6));

		Assert.Equal(3, fired);
	}

	[Fact]
	public void TheCallback_SeesTheTimeOfItsDueMoment()
	{
		var time = new ManualTimeProvider();
		List<TimeSpan> seen = [];
		var start = time.GetTimestamp();
		using var timer = time.CreateTimer(
			_ => seen.Add(time.GetElapsedTime(start, time.GetTimestamp())), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

		time.Advance(TimeSpan.FromSeconds(5));

		Assert.Equal<TimeSpan>([TimeSpan.FromSeconds(2)], seen);
	}

	[Fact]
	public void ATimerCanBeRescheduledFromItsOwnCallback()
	{
		var time = new ManualTimeProvider();
		var fired = 0;
		ITimer? timer = null;
		timer = time.CreateTimer(
			_ =>
			{
				fired++;
				if (fired < 3)
				{
					timer!.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
				}
			},
			null,
			TimeSpan.FromSeconds(1),
			Timeout.InfiniteTimeSpan);

		time.Advance(TimeSpan.FromSeconds(10));

		Assert.Equal(3, fired);
		timer.Dispose();
	}

	[Fact]
	public void ADisposedTimer_NeverFires()
	{
		var time = new ManualTimeProvider();
		var fired = 0;
		var timer = time.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

		timer.Dispose();
		time.Advance(TimeSpan.FromSeconds(5));

		Assert.Equal(0, fired);
		Assert.False(timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan));
	}

	[Fact]
	public void Advance_RejectsNegativeDelta()
	{
		var time = new ManualTimeProvider();

		Assert.Throws<ArgumentOutOfRangeException>(() => time.Advance(TimeSpan.FromSeconds(-1)));
	}
}

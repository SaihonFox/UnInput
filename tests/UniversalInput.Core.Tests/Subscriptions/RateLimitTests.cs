using UniversalInput.Core.Diagnostics;
using UniversalInput.Core.Events;
using UniversalInput.Core.Subscriptions;
using UniversalInput.Testing.Harness;

namespace UniversalInput.Core.Tests.Subscriptions;

public class RateLimitTests
{
	private static readonly DeviceId Mouse = new(1);
	private static readonly DeviceId OtherMouse = new(2);
	private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

	private static (Subscription Subscription, ManualTimeProvider Time) Create(double rate = 10, int capacity = 256)
	{
		var time = new ManualTimeProvider();
		var options = new SubscriptionOptions { Capacity = capacity, MaxContinuousRate = rate };
		var filter = EventFilter.Create(options, new Dictionary<string, ushort>());
		return (new Subscription(options, filter, new InputMetrics(), _ => { }, time), time);
	}

	private static List<InputEvent> Drain(Subscription subscription)
	{
		List<InputEvent> events = [];
		while (subscription.Reader.TryRead(out var inputEvent))
		{
			events.Add(inputEvent);
		}

		return events;
	}

	[Fact]
	public void TheFirstEventPasses_AndTheRestAreMergedUntilTheIntervalEnds()
	{
		var (subscription, time) = Create();

		subscription.Offer(InputEvent.MouseMove(Mouse, 1, 0), null);
		subscription.Offer(InputEvent.MouseMove(Mouse, 1, 0), null);
		subscription.Offer(InputEvent.MouseMove(Mouse, 1, 0), null);
		var immediate = Drain(subscription);
		time.Advance(Interval);
		var later = Drain(subscription);

		Assert.Equal(1f, Assert.Single(immediate).V0);
		var merged = Assert.Single(later);
		Assert.Equal(2f, merged.V0);
		Assert.True((merged.Flags & InputEventFlags.Coalesced) != 0);
	}

	[Fact]
	public void NothingIsHeldBack_WhenEventsComeSlowerThanTheLimit()
	{
		var (subscription, time) = Create();

		for (var i = 0; i < 5; i++)
		{
			subscription.Offer(InputEvent.MouseMove(Mouse, 1, 0), null);
			time.Advance(Interval);
		}

		Assert.Equal(5, Drain(subscription).Count);
	}

	[Fact]
	public void AFastStream_IsLimitedButLosesNoRelativeMovement()
	{
		var (subscription, time) = Create();
		List<InputEvent> events = [];

		for (var i = 0; i < 100; i++)
		{
			subscription.Offer(InputEvent.MouseMove(Mouse, 1, 0), null);
			time.Advance(TimeSpan.FromMilliseconds(10));
			events.AddRange(Drain(subscription));
		}

		time.Advance(Interval);
		events.AddRange(Drain(subscription));

		Assert.InRange(events.Count, 9, 12);
		Assert.Equal(100f, events.Sum(e => e.V0));
	}

	[Fact]
	public void AbsolutePositions_KeepOnlyTheLatestValue()
	{
		var (subscription, time) = Create();

		subscription.Offer(InputEvent.MouseMove(Mouse, 10, 10, absolute: true), null);
		subscription.Offer(InputEvent.MouseMove(Mouse, 20, 20, absolute: true), null);
		subscription.Offer(InputEvent.MouseMove(Mouse, 30, 40, absolute: true), null);
		Drain(subscription);
		time.Advance(Interval);

		var last = Assert.Single(Drain(subscription));
		Assert.Equal(30f, last.V0);
		Assert.Equal(40f, last.V1);
	}

	[Fact]
	public void ADiscreteEvent_ReleasesPendingMovementFirst()
	{
		var (subscription, _) = Create();

		subscription.Offer(InputEvent.MouseMove(Mouse, 1, 0), null);
		subscription.Offer(InputEvent.MouseMove(Mouse, 5, 0), null);
		subscription.Offer(InputEvent.MouseDown(Mouse, 1), null);
		var events = Drain(subscription);

		Assert.Equal<InputEventKind>(
			[InputEventKind.MouseMoved, InputEventKind.MouseMoved, InputEventKind.MouseButtonPressed],
			events.Select(e => e.Kind));
		Assert.Equal(5f, events[1].V0);
	}

	[Fact]
	public void DiscreteEvents_AreNotLimited()
	{
		var (subscription, _) = Create();

		for (var i = 0; i < 20; i++)
		{
			subscription.Offer(InputEvent.KeyDown(Mouse, i), null);
		}

		Assert.Equal(20, Drain(subscription).Count);
	}

	[Fact]
	public void DevicesAndAxes_AreLimitedIndependently()
	{
		var (subscription, _) = Create();

		subscription.Offer(InputEvent.MouseMove(Mouse, 1, 0), null);
		subscription.Offer(InputEvent.MouseMove(OtherMouse, 1, 0), null);
		subscription.Offer(InputEvent.Axis(Mouse, 0, 0.5f), null);
		subscription.Offer(InputEvent.Axis(Mouse, 1, 0.5f), null);

		Assert.Equal(4, Drain(subscription).Count);
	}

	[Fact]
	public void Completing_ReleasesWhatIsStillPending()
	{
		var (subscription, _) = Create();
		subscription.Offer(InputEvent.MouseMove(Mouse, 1, 0), null);
		subscription.Offer(InputEvent.MouseMove(Mouse, 4, 0), null);

		subscription.Complete();

		var events = Drain(subscription);
		Assert.Equal<float>([1f, 4f], events.Select(e => e.V0));
	}

	[Fact]
	public async Task AReaderWaitingForData_IsWokenByTheTimer()
	{
		var (subscription, time) = Create();
		subscription.Offer(InputEvent.MouseMove(Mouse, 1, 0), null);
		subscription.Offer(InputEvent.MouseMove(Mouse, 2, 0), null);
		Drain(subscription);

		var waiting = subscription.Reader.WaitToReadAsync(TestContext.Current.CancellationToken).AsTask();
		Assert.False(waiting.IsCompleted);
		time.Advance(Interval);

		Assert.True(await waiting);
		Assert.Equal(2f, Assert.Single(Drain(subscription)).V0);
	}

	[Fact]
	public void ADisconnect_ForgetsTheDeviceStreams()
	{
		var (subscription, time) = Create();
		subscription.Offer(InputEvent.MouseMove(Mouse, 1, 0), null);
		subscription.Offer(InputEvent.Disconnected(Mouse), null);
		Drain(subscription);

		subscription.Offer(InputEvent.MouseMove(Mouse, 1, 0), null);

		Assert.Single(Drain(subscription));
		time.Advance(Interval);
		Assert.Empty(Drain(subscription));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	public void AnInvalidRate_IsRejected(double rate)
	{
		var options = new SubscriptionOptions { MaxContinuousRate = rate };
		var filter = EventFilter.Create(options, new Dictionary<string, ushort>());

		Assert.Throws<ArgumentOutOfRangeException>(
			() => new Subscription(options, filter, new InputMetrics(), _ => { }));
	}
}

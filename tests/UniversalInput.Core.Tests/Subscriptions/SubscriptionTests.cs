using UniversalInput.Core.Diagnostics;
using UniversalInput.Core.Events;
using UniversalInput.Core.Subscriptions;

namespace UniversalInput.Core.Tests.Subscriptions;

public class SubscriptionTests
{
	private static readonly DeviceId Device = new(1);

	private static Subscription Create(SubscriptionOptions? options = null, Action<Subscription>? onDispose = null)
	{
		options ??= new SubscriptionOptions();
		var filter = EventFilter.Create(options, new Dictionary<string, ushort>());
		return new Subscription(options, filter, new InputMetrics(), onDispose ?? (_ => { }));
	}

	private static InputEvent Key(int usage) => InputEvent.KeyDown(Device, usage).WithSequence(usage);

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
	public void Events_AreReadInTheOrderTheyWereOffered()
	{
		var subscription = Create();

		for (var i = 1; i <= 5; i++)
		{
			subscription.Offer(Key(i), null);
		}

		Assert.Equal<int>([1, 2, 3, 4, 5], Drain(subscription).Select(e => e.Int0));
	}

	[Fact]
	public void Capacity_BelowTwo_IsRejected()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Create(new SubscriptionOptions { Capacity = 1 }));
	}

	[Fact]
	public void ContinuousEvents_AreCoalescedWhenFull_WithoutLosingRelativeMotion()
	{
		var subscription = Create(new SubscriptionOptions { Capacity = 4 });

		for (var i = 1; i <= 10; i++)
		{
			subscription.Offer(InputEvent.MouseMove(Device, 1, 2).WithSequence(i), null);
		}

		var events = Drain(subscription);

		Assert.Equal(4, events.Count);
		Assert.Equal(10f, events.Sum(e => e.V0));
		Assert.Equal(20f, events.Sum(e => e.V1));
		Assert.True((events[^1].Flags & InputEventFlags.Coalesced) != 0);
		Assert.Equal(0, subscription.DroppedCount);
	}

	[Fact]
	public void Coalescing_DoesNotCrossADiscreteEventOfTheSameDevice()
	{
		var subscription = Create(new SubscriptionOptions { Capacity = 3 });

		subscription.Offer(InputEvent.MouseMove(Device, 1, 0), null);
		subscription.Offer(InputEvent.MouseDown(Device, 1), null);
		subscription.Offer(InputEvent.MouseMove(Device, 1, 0), null);

		// Очередь полна. Новое движение можно склеить только с последним движением, а не с тем, что было до клика.
		subscription.Offer(InputEvent.MouseMove(Device, 1, 0), null);

		var events = Drain(subscription);
		Assert.Equal<InputEventKind>(
			[InputEventKind.MouseMoved, InputEventKind.MouseButtonPressed, InputEventKind.MouseMoved],
			events.Select(e => e.Kind));
		Assert.Equal(1f, events[0].V0);
		Assert.Equal(2f, events[2].V0);
	}

	[Fact]
	public void DropOldest_ReplacesTheOldestEventsWithAGap()
	{
		var subscription = Create(new SubscriptionOptions { Capacity = 4, Overflow = OverflowPolicy.DropOldest });

		for (var i = 1; i <= 6; i++)
		{
			subscription.Offer(Key(i), null);
		}

		var events = Drain(subscription);

		Assert.Equal(4, events.Count);
		Assert.Equal(new StreamGapEvent(3, StreamGapReason.SubscriptionOverflow), events[0].AsStreamGap());
		Assert.Equal<int>([4, 5, 6], events.Skip(1).Select(e => e.Int0));
		Assert.Equal(3, subscription.DroppedCount);
	}

	[Fact]
	public void DropNewest_KeepsOldEventsAndMarksTheLoss()
	{
		var subscription = Create(new SubscriptionOptions { Capacity = 4, Overflow = OverflowPolicy.DropNewest });

		for (var i = 1; i <= 6; i++)
		{
			subscription.Offer(Key(i), null);
		}

		var events = Drain(subscription);

		Assert.Equal(4, events.Count);
		Assert.Equal<int>([1, 2, 3], events.Take(3).Select(e => e.Int0));
		Assert.Equal(InputEventKind.StreamGap, events[3].Kind);
		Assert.Equal(StreamGapReason.SubscriptionDroppedNewest, events[3].AsStreamGap().Reason);
		Assert.Equal(3, events[3].AsStreamGap().Lost);
		Assert.Equal(3, subscription.DroppedCount);
	}

	[Fact]
	public async Task FaultSubscription_CompletesWithErrorAfterDeliveringQueuedEvents()
	{
		var subscription = Create(new SubscriptionOptions { Capacity = 2, Overflow = OverflowPolicy.FaultSubscription });

		subscription.Offer(Key(1), null);
		subscription.Offer(Key(2), null);
		subscription.Offer(Key(3), null);

		Assert.True(subscription.Reader.TryRead(out var first));
		Assert.True(subscription.Reader.TryRead(out var second));
		Assert.Equal<int>([1, 2], [first.Int0, second.Int0]);

		await Assert.ThrowsAsync<InvalidOperationException>(() => subscription.Reader.ReadAsync().AsTask());
		await Assert.ThrowsAsync<InvalidOperationException>(() => subscription.Reader.Completion);
	}

	[Fact]
	public void Kinds_FilterOutOtherEvents_ButStreamGapAlwaysPasses()
	{
		var subscription = Create(new SubscriptionOptions { Kinds = [InputEventKind.KeyPressed] });

		subscription.Offer(InputEvent.MouseMove(Device, 1, 1), null);
		subscription.Offer(Key(1), null);
		subscription.Offer(InputEvent.Gap(2, StreamGapReason.BackendQueueOverflow), null);

		Assert.Equal<InputEventKind>(
			[InputEventKind.KeyPressed, InputEventKind.StreamGap],
			Drain(subscription).Select(e => e.Kind));
	}

	[Fact]
	public void Devices_FilterBySource()
	{
		var subscription = Create(new SubscriptionOptions { Devices = [new DeviceId(2)] });

		subscription.Offer(InputEvent.KeyDown(new DeviceId(1), 1), null);
		subscription.Offer(InputEvent.KeyDown(new DeviceId(2), 2), null);

		Assert.Equal<int>([2], Drain(subscription).Select(e => e.Int0));
	}

	[Fact]
	public void DeviceKinds_FilterUsesTheDeviceThatWasPassedIn()
	{
		var keyboard = new InputDevice
		{
			Id = Device,
			Key = new DeviceKey("k"),
			Name = "k",
			Kind = InputDeviceKind.Keyboard,
			Origin = DeviceOrigin.Physical,
			BackendId = "b",
		};
		var subscription = Create(new SubscriptionOptions { DeviceKinds = [InputDeviceKind.Mouse] });

		subscription.Offer(Key(1), keyboard);
		subscription.Offer(Key(2), null);

		Assert.Empty(Drain(subscription));
	}

	[Fact]
	public void Predicate_IsApplied_AndItsExceptionsAreTreatedAsRejection()
	{
		var subscription = Create(new SubscriptionOptions
		{
			Predicate = e => e.Int0 == 2 ? true : throw new InvalidOperationException("boom"),
		});

		subscription.Offer(Key(1), null);
		subscription.Offer(Key(2), null);

		Assert.Equal<int>([2], Drain(subscription).Select(e => e.Int0));
	}

	[Fact]
	public void UnknownBackendId_IsRejected()
	{
		var options = new SubscriptionOptions { BackendIds = ["nope"] };

		Assert.Throws<ArgumentException>(() => EventFilter.Create(options, new Dictionary<string, ushort>()));
	}

	[Fact]
	public async Task WaitToRead_CompletesWhenAnEventArrives()
	{
		var subscription = Create();

		var wait = subscription.Reader.WaitToReadAsync().AsTask();
		Assert.False(wait.IsCompleted);

		subscription.Offer(Key(1), null);

		Assert.True(await wait.WaitAsync(TimeSpan.FromSeconds(2)));
	}

	[Fact]
	public async Task WaitToRead_ObservesCancellation()
	{
		var subscription = Create();
		using var cts = new CancellationTokenSource();

		var wait = subscription.Reader.WaitToReadAsync(cts.Token).AsTask();
		cts.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
	}

	[Fact]
	public async Task Complete_LetsTheReaderDrainQueuedEventsAndThenEnds()
	{
		var subscription = Create();
		subscription.Offer(Key(1), null);
		subscription.Offer(Key(2), null);

		subscription.Complete();
		subscription.Offer(Key(3), null);

		List<int> received = [];
		await foreach (var inputEvent in subscription.Reader.ReadAllAsync())
		{
			received.Add(inputEvent.Int0);
		}

		Assert.Equal<int>([1, 2], received);
		await subscription.Reader.Completion.WaitAsync(TimeSpan.FromSeconds(2));
	}

	[Fact]
	public async Task Complete_WakesAPendingReader()
	{
		var subscription = Create();
		var wait = subscription.Reader.WaitToReadAsync().AsTask();

		subscription.Complete();

		Assert.False(await wait.WaitAsync(TimeSpan.FromSeconds(2)));
	}

	[Fact]
	public async Task DisposeAsync_UnsubscribesAndCompletes()
	{
		Subscription? disposed = null;
		var subscription = Create(onDispose: s => disposed = s);

		await subscription.DisposeAsync();

		Assert.Same(subscription, disposed);
		await subscription.Reader.Completion.WaitAsync(TimeSpan.FromSeconds(2));
	}
}

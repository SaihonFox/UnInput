using UniversalInput.Core.Events;

namespace UniversalInput.Core.Tests.Events;

public class EventCoalescerTests
{
	private static readonly DeviceId Device = new(1);

	[Fact]
	public void RelativeMouseMoves_AreSummed()
	{
		var first = InputEvent.MouseMove(Device, 2, 3).WithSequence(1);
		var second = InputEvent.MouseMove(Device, 5, -1).WithSequence(2);

		Assert.True(EventCoalescer.TryMerge(in first, in second, out var merged));

		Assert.Equal(7f, merged.V0);
		Assert.Equal(2f, merged.V1);
		Assert.Equal(2, merged.Sequence);
		Assert.True((merged.Flags & InputEventFlags.Coalesced) != 0);
	}

	[Fact]
	public void AbsoluteMouseMoves_KeepTheLatest()
	{
		var first = InputEvent.MouseMove(Device, 10, 10, absolute: true).WithSequence(1);
		var second = InputEvent.MouseMove(Device, 20, 30, absolute: true).WithSequence(2);

		Assert.True(EventCoalescer.TryMerge(in first, in second, out var merged));

		Assert.Equal(20f, merged.V0);
		Assert.Equal(30f, merged.V1);
		Assert.True((merged.Flags & InputEventFlags.Absolute) != 0);
		Assert.True((merged.Flags & InputEventFlags.Coalesced) != 0);
	}

	[Fact]
	public void RelativeAndAbsoluteMoves_AreNotMerged()
	{
		var relative = InputEvent.MouseMove(Device, 1, 1);
		var absolute = InputEvent.MouseMove(Device, 5, 5, absolute: true);

		Assert.False(EventCoalescer.TryMerge(in relative, in absolute, out _));
	}

	[Fact]
	public void Wheel_DeltasAreSummed()
	{
		var first = InputEvent.Wheel(Device, 0, 1);
		var second = InputEvent.Wheel(Device, 0, 2);

		Assert.True(EventCoalescer.TryMerge(in first, in second, out var merged));

		Assert.Equal(3f, merged.V1);
	}

	[Fact]
	public void SameAxis_KeepsTheLatestValue()
	{
		var first = InputEvent.Axis(Device, 1, 0.1f);
		var second = InputEvent.Axis(Device, 1, 0.9f);

		Assert.True(EventCoalescer.TryMerge(in first, in second, out var merged));

		Assert.Equal(0.9f, merged.V0);
	}

	[Fact]
	public void DifferentAxes_AreNotMerged()
	{
		var first = InputEvent.Axis(Device, 1, 0.1f);
		var second = InputEvent.Axis(Device, 2, 0.9f);

		Assert.False(EventCoalescer.TryMerge(in first, in second, out _));
	}

	[Fact]
	public void SameTouch_KeepsTheLatestPosition()
	{
		var first = InputEvent.TouchMove(Device, 3, 1, 1);
		var second = InputEvent.TouchMove(Device, 3, 8, 9);

		Assert.True(EventCoalescer.TryMerge(in first, in second, out var merged));

		Assert.Equal(8f, merged.V0);
		Assert.Equal(9f, merged.V1);
	}

	[Fact]
	public void DifferentDevices_AreNotMerged()
	{
		var first = InputEvent.MouseMove(new DeviceId(1), 1, 1);
		var second = InputEvent.MouseMove(new DeviceId(2), 1, 1);

		Assert.False(EventCoalescer.TryMerge(in first, in second, out _));
	}

	[Fact]
	public void DiscreteEvents_AreNeverMerged()
	{
		var first = InputEvent.KeyDown(Device, KeyCodes.A);
		var second = InputEvent.KeyDown(Device, KeyCodes.A);

		Assert.False(EventCoalescer.TryMerge(in first, in second, out _));
	}
}

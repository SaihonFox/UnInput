namespace UniversalInput.Core.Tests.Events;

public class InputEventTests
{
	private static readonly DeviceId Device = new(1);

	[Fact]
	public void KeyDown_RoundTripsThroughKeyView()
	{
		var inputEvent = InputEvent.KeyDown(Device, KeyCodes.A, 'a', KeyModifiers.Shift, repeat: true);

		Assert.Equal(InputEventKind.KeyPressed, inputEvent.Kind);
		Assert.Equal(new KeyEvent(KeyCodes.A, 'a', KeyModifiers.Shift, true), inputEvent.AsKey());
	}

	[Fact]
	public void MouseMove_AbsoluteFlagIsExposedByPointerView()
	{
		var relative = InputEvent.MouseMove(Device, 3, 4);
		var absolute = InputEvent.MouseMove(Device, 3, 4, absolute: true);

		Assert.False(relative.AsPointer().IsAbsolute);
		Assert.True(absolute.AsPointer().IsAbsolute);
		Assert.Equal(new PointerEvent(3, 4, true), absolute.AsPointer());
	}

	[Fact]
	public void Axis_Touch_Sensor_ReadBackTheirFields()
	{
		Assert.Equal(new AxisEvent(2, 0.5f), InputEvent.Axis(Device, 2, 0.5f).AsAxis());
		Assert.Equal(new TouchEvent(7, 1, 2, 0.25f), InputEvent.TouchStart(Device, 7, 1, 2, 0.25f).AsTouch());
		Assert.Equal(
			new SensorEvent(SensorKind.Gyroscope, 1, 2, 3),
			InputEvent.Sensor(Device, SensorKind.Gyroscope, 1, 2, 3).AsSensor());
	}

	[Fact]
	public void Gap_CarriesLostCountAndReason()
	{
		var gap = InputEvent.Gap(5, StreamGapReason.SubscriptionOverflow);

		Assert.Equal(InputEventKind.StreamGap, gap.Kind);
		Assert.Equal(new StreamGapEvent(5, StreamGapReason.SubscriptionOverflow), gap.AsStreamGap());
	}

	[Theory]
	[InlineData(InputEventKind.MouseMoved, true)]
	[InlineData(InputEventKind.MouseWheel, true)]
	[InlineData(InputEventKind.GamepadAxisChanged, true)]
	[InlineData(InputEventKind.TouchMoved, true)]
	[InlineData(InputEventKind.SensorChanged, true)]
	[InlineData(InputEventKind.KeyPressed, false)]
	[InlineData(InputEventKind.MouseButtonPressed, false)]
	[InlineData(InputEventKind.TouchStarted, false)]
	[InlineData(InputEventKind.DeviceConnected, false)]
	[InlineData(InputEventKind.StreamGap, false)]
	public void IsContinuous_MatchesEventKind(InputEventKind kind, bool expected)
	{
		var inputEvent = new InputEvent { Kind = kind };

		Assert.Equal(expected, inputEvent.IsContinuous);
	}

	[Fact]
	public void WithSequence_DoesNotChangeOtherFields()
	{
		var original = InputEvent.KeyDown(Device, KeyCodes.B);
		var stamped = original.WithSequence(42);

		Assert.Equal(42, stamped.Sequence);
		Assert.Equal(original with { Sequence = 42 }, stamped);
	}

	[Fact]
	public void Timestamp_DefaultsToNow_AndCanBeSet()
	{
		Assert.NotEqual(0, InputEvent.KeyDown(Device, KeyCodes.A).Timestamp);
		Assert.Equal(123, InputEvent.KeyDown(Device, KeyCodes.A, timestamp: 123).Timestamp);
	}
}

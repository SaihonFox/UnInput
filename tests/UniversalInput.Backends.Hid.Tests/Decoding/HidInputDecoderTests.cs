using UniversalInput.Backends.Hid.Decoding;
using UniversalInput.Backends.Hid.Tests.Fakes;

namespace UniversalInput.Backends.Hid.Tests.Decoding;

public class HidInputDecoderTests
{
	private static readonly DeviceId Device = new(7);

	private static List<InputEvent> Decode(HidInputDecoder decoder, params byte[][] reports)
	{
		List<InputEvent> events = [];
		foreach (var report in reports)
		{
			decoder.Decode(Device, report, events.Add);
		}

		return events;
	}

	private static HidInputDecoder Keyboard() => HidInputDecoder.TryCreate(HidTestDescriptors.BootKeyboard)!;

	private static HidInputDecoder Mouse() => HidInputDecoder.TryCreate(HidTestDescriptors.WheelMouse)!;

	[Fact]
	public void AGamepad_OrAnEmptyDescriptor_HasNoDecoder()
	{
		Assert.Null(HidInputDecoder.TryCreate(HidTestDescriptors.Gamepad));
		Assert.Null(HidInputDecoder.TryCreate([]));
	}

	[Fact]
	public void Decoders_ReportWhichClassTheyCover_AndHowLongAReportIs()
	{
		var keyboard = Keyboard();
		Assert.True(keyboard.HasKeyboard);
		Assert.False(keyboard.HasMouse);
		Assert.Equal(9, keyboard.MaxReportLength);

		var mouse = Mouse();
		Assert.True(mouse.HasMouse);
		Assert.False(mouse.HasKeyboard);
		Assert.Equal(5, mouse.MaxReportLength);
	}

	[Fact]
	public void KeyPress_AndRelease_AreReportedAsUsagesWithoutTranslation()
	{
		var events = Decode(
			Keyboard(),
			[0, 0, 0, 0x04, 0, 0, 0, 0, 0],
			[0, 0, 0, 0, 0, 0, 0, 0, 0]);

		Assert.Equal<InputEventKind>([InputEventKind.KeyPressed, InputEventKind.KeyReleased], events.Select(e => e.Kind));
		Assert.All(events, e => Assert.Equal(0x04, e.Int0));
		Assert.All(events, e => Assert.Equal(Device, e.Device));
	}

	[Fact]
	public void Modifiers_AreKeysToo_AndFlagTheKeysPressedAfterThem()
	{
		var events = Decode(
			Keyboard(),
			[0, 0b0000_0010, 0, 0, 0, 0, 0, 0, 0],
			[0, 0b0000_0010, 0, 0x04, 0, 0, 0, 0, 0],
			[0, 0, 0, 0x04, 0, 0, 0, 0, 0]);

		Assert.Equal<int>([0xE1, 0x04, 0xE1], events.Select(e => e.Int0));
		Assert.Equal<InputEventKind>(
			[InputEventKind.KeyPressed, InputEventKind.KeyPressed, InputEventKind.KeyReleased], events.Select(e => e.Kind));
		Assert.Equal(KeyModifiers.Shift, events[1].Modifiers);
		Assert.Equal(KeyModifiers.None, events[2].Modifiers);
	}

	[Fact]
	public void ChangesBetweenReports_AreADiff_ReleasesFirst()
	{
		var events = Decode(
			Keyboard(),
			[0, 0, 0, 0x04, 0x05, 0, 0, 0, 0],
			[0, 0, 0, 0x05, 0x06, 0, 0, 0, 0]);

		Assert.Equal<string>(
			["down:4", "down:5", "up:4", "down:6"],
			events.Select(e => (e.Kind == InputEventKind.KeyPressed ? "down:" : "up:") + e.Int0));
	}

	[Fact]
	public void ARepeatedReport_ProducesNothing()
	{
		var events = Decode(
			Keyboard(),
			[0, 0, 0, 0x04, 0, 0, 0, 0, 0],
			[0, 0, 0, 0x04, 0, 0, 0, 0, 0]);

		Assert.Single(events);
	}

	[Fact]
	public void RollOverError_IsNotAKey()
	{
		var events = Decode(Keyboard(), [0, 0, 0, 1, 1, 1, 1, 1, 1]);

		Assert.Empty(events);
	}

	[Fact]
	public void AShortOrEmptyReport_IsIgnored_WithoutFalseReleases()
	{
		var decoder = Keyboard();

		var events = Decode(
			decoder,
			[0, 0, 0, 0x04, 0, 0, 0, 0, 0],
			[0, 0, 0],
			[],
			[0, 0, 0, 0x04, 0, 0, 0, 0, 0]);

		Assert.Equal(InputEventKind.KeyPressed, Assert.Single(events).Kind);
	}

	[Fact]
	public void AReportWithAnUnknownId_IsIgnored() =>
		Assert.Empty(Decode(Keyboard(), [5, 0, 0, 0x04, 0, 0, 0, 0, 0]));

	[Fact]
	public void NumberedReports_AreDecodedPerId_AndOthersAreIgnored()
	{
		var decoder = HidInputDecoder.TryCreate(HidTestDescriptors.NumberedKeyboard)!;

		var events = Decode(
			decoder,
			[1, 0, 0x04, 0],
			[2, 0xFF],
			[1, 0, 0, 0]);

		Assert.Equal<InputEventKind>([InputEventKind.KeyPressed, InputEventKind.KeyReleased], events.Select(e => e.Kind));
		Assert.Equal(4, decoder.MaxReportLength);
	}

	[Fact]
	public void MouseButtons_AreReportedByNumber()
	{
		var events = Decode(
			Mouse(),
			[0, 0b001, 0, 0, 0],
			[0, 0b101, 0, 0, 0],
			[0, 0b100, 0, 0, 0]);

		Assert.Equal<string>(
			["down:1", "down:3", "up:1"],
			events.Select(e => (e.Kind == InputEventKind.MouseButtonPressed ? "down:" : "up:") + e.Int0));
	}

	[Fact]
	public void MouseMotion_IsRelative_AndSigned()
	{
		var events = Decode(Mouse(), [0, 0, 10, 0xFB, 0], [0, 0, 0, 0, 0]);

		var move = Assert.Single(events);
		Assert.Equal(InputEventKind.MouseMoved, move.Kind);
		Assert.Equal(10f, move.V0);
		Assert.Equal(-5f, move.V1);
		Assert.Equal(InputEventFlags.None, move.Flags & InputEventFlags.Absolute);
	}

	[Fact]
	public void TheWheel_IsReportedAsVerticalScroll_PositiveUp()
	{
		var events = Decode(Mouse(), [0, 0, 0, 0, 1], [0, 0, 0, 0, 0xFF]);

		Assert.Equal<InputEventKind>([InputEventKind.MouseWheel, InputEventKind.MouseWheel], events.Select(e => e.Kind));
		Assert.Equal(1f, events[0].V1);
		Assert.Equal(-1f, events[1].V1);
		Assert.All(events, e => Assert.Equal(0f, e.V0));
	}

	[Fact]
	public void AMovementWithAClick_ProducesMotionFirst_ThenTheButton()
	{
		var events = Decode(Mouse(), [0, 0b001, 3, 4, 0]);

		Assert.Equal<InputEventKind>([InputEventKind.MouseMoved, InputEventKind.MouseButtonPressed], events.Select(e => e.Kind));
	}

	[Fact]
	public void AbsoluteAxes_AreNotReportedAsMotion()
	{
		// Тот же мышиный дескриптор, но у осей флаг Input — Absolute (0x02 вместо 0x06): такие оси не разбираются.
		var descriptor = HidTestDescriptors.WheelMouse.ToArray();
		var flagIndex = Array.LastIndexOf(descriptor, (byte)0x06);
		descriptor[flagIndex] = 0x02;

		var decoder = HidInputDecoder.TryCreate(descriptor)!;

		Assert.Empty(Decode(decoder, [0, 0, 10, 20, 0]));
	}
}

using UniversalInput.Backends.Hid.Descriptors;
using UniversalInput.Backends.Hid.Discovery;

namespace UniversalInput.Backends.Hid.Tests.Discovery;

public class HidDeviceClassifierTests
{
	[Theory]
	[InlineData(0x01, 0x06, "keyboard")]
	[InlineData(0x01, 0x07, "keyboard")]
	[InlineData(0x01, 0x02, "mouse")]
	[InlineData(0x01, 0x04, "joystick")]
	[InlineData(0x01, 0x05, "gamepad")]
	[InlineData(0x0D, 0x04, "touch")]
	[InlineData(0x0D, 0x05, "touch")]
	[InlineData(0x0D, 0x02, "hid")]
	[InlineData(0x0C, 0x01, "hid")]
	[InlineData(0xFF00, 0x01, "hid")]
	public void KindOf_FollowsTheTopLevelUsage(int page, int usage, string expected)
	{
		Assert.Equal(expected, HidDeviceClassifier.KindOf([new HidUsage(page, usage)]).Name);
	}

	[Fact]
	public void KindOf_WithoutUsages_IsGenericHid()
	{
		Assert.Equal(InputDeviceKind.Hid, HidDeviceClassifier.KindOf([]));
	}

	[Fact]
	public void KindOf_TakesTheFirstRecognizedCollection()
	{
		HidUsage[] usages = [new HidUsage(0xFF00, 1), new HidUsage(0x01, 0x02), new HidUsage(0x01, 0x06)];

		Assert.Equal(InputDeviceKind.Mouse, HidDeviceClassifier.KindOf(usages));
	}

	[Fact]
	public void Capabilities_AlwaysIncludeRawReports_AndOnlyWhatTheClassImplies()
	{
		Assert.Equal(InputCapabilities.RawReports, HidDeviceClassifier.CapabilitiesOf([]));
		Assert.Equal(
			InputCapabilities.RawReports | InputCapabilities.Keys,
			HidDeviceClassifier.CapabilitiesOf([new HidUsage(0x01, 0x06)]));
		Assert.Equal(
			InputCapabilities.RawReports | InputCapabilities.PointerMotion | InputCapabilities.PointerButtons,
			HidDeviceClassifier.CapabilitiesOf([new HidUsage(0x01, 0x02)]));
		Assert.Equal(
			InputCapabilities.RawReports | InputCapabilities.GamepadButtons | InputCapabilities.Axes,
			HidDeviceClassifier.CapabilitiesOf([new HidUsage(0x01, 0x05)]));
		Assert.Equal(
			InputCapabilities.RawReports | InputCapabilities.Touch,
			HidDeviceClassifier.CapabilitiesOf([new HidUsage(0x0D, 0x05)]));
	}

	[Fact]
	public void Capabilities_OfACompositeDevice_AreCombined()
	{
		var result = HidDeviceClassifier.CapabilitiesOf([new HidUsage(0x01, 0x06), new HidUsage(0x01, 0x02)]);

		Assert.True(result.HasFlag(InputCapabilities.Keys));
		Assert.True(result.HasFlag(InputCapabilities.PointerMotion));
	}
}

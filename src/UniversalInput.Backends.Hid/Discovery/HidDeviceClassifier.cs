using UniversalInput.Backends.Hid.Descriptors;

namespace UniversalInput.Backends.Hid.Discovery;

/// <summary>
/// Определяет вид устройства по применениям из дескриптора. Состав возможностей — по классу устройства:
/// оси, колесо и число кнопок из дескриптора не извлекаются, поэтому здесь они не заявляются.
/// </summary>
internal static class HidDeviceClassifier
{
	private const int GenericDesktop = 0x01;
	private const int Digitizers = 0x0D;

	public static InputDeviceKind KindOf(IReadOnlyList<HidUsage> usages)
	{
		foreach (var usage in usages)
		{
			var kind = KindOf(usage);
			if (kind is { } known)
			{
				return known;
			}
		}

		return InputDeviceKind.Hid;
	}

	public static InputCapabilities CapabilitiesOf(IReadOnlyList<HidUsage> usages)
	{
		var result = InputCapabilities.RawReports;
		foreach (var usage in usages)
		{
			result |= usage switch
			{
				{ Page: GenericDesktop, Usage: 0x06 or 0x07 } => InputCapabilities.Keys,
				{ Page: GenericDesktop, Usage: 0x02 } => InputCapabilities.PointerMotion | InputCapabilities.PointerButtons,
				{ Page: GenericDesktop, Usage: 0x04 or 0x05 } => InputCapabilities.GamepadButtons | InputCapabilities.Axes,
				{ Page: Digitizers, Usage: 0x04 or 0x05 } => InputCapabilities.Touch,
				_ => InputCapabilities.None,
			};
		}

		return result;
	}

	private static InputDeviceKind? KindOf(HidUsage usage) => usage switch
	{
		{ Page: GenericDesktop, Usage: 0x06 or 0x07 } => InputDeviceKind.Keyboard,
		{ Page: GenericDesktop, Usage: 0x02 } => InputDeviceKind.Mouse,
		{ Page: GenericDesktop, Usage: 0x04 } => InputDeviceKind.Joystick,
		{ Page: GenericDesktop, Usage: 0x05 } => InputDeviceKind.Gamepad,
		{ Page: Digitizers, Usage: 0x04 or 0x05 } => InputDeviceKind.Touch,
		_ => null,
	};
}

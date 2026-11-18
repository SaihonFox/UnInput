using SharpHook.Data;
using UniversalInput.Backends.GlobalHook.Emulation;

namespace UniversalInput.Backends.GlobalHook.Tests.Emulation;

public class SharpHookEmulatorTests
{
	[Theory]
	[InlineData(UioHookResult.Success, "Success")]
	[InlineData(UioHookResult.Failure, "Failed")]
	[InlineData(UioHookResult.ErrorOutOfMemory, "Failed")]
	[InlineData(UioHookResult.ErrorUnsupportedFeature, "Unsupported")]
	[InlineData(UioHookResult.ErrorAxApiDisabled, "AccessibilityDenied")]
	[InlineData(UioHookResult.ErrorAxApiRevoked, "AccessibilityDenied")]
	[InlineData(UioHookResult.ErrorLinuxOpenUinput, "UinputUnavailable")]
	[InlineData(UioHookResult.ErrorLinuxCreateUinputDevice, "UinputUnavailable")]
	[InlineData(UioHookResult.ErrorLinuxWriteUinput, "UinputUnavailable")]
	[InlineData(UioHookResult.ErrorLinuxVirtualDevicesNoInitialized, "UinputUnavailable")]
	[InlineData(UioHookResult.ErrorXOpenDisplay, "DisplayUnavailable")]
	[InlineData(UioHookResult.ErrorLinuxOpenWaylandDisplay, "DisplayUnavailable")]
	[InlineData(UioHookResult.ErrorSetWindowsHookEx, "Failed")]
	public void LibraryResults_AreTranslatedToEmulationStatuses(UioHookResult result, string expected) =>
		Assert.Equal(Enum.Parse<EmulationStatus>(expected), SharpHookEmulator.Translate(result));

	[Fact]
	public void OnlySuccess_IsTranslatedToSuccess()
	{
		foreach (var result in Enum.GetValues<UioHookResult>())
		{
			var status = SharpHookEmulator.Translate(result);
			Assert.Equal(result == UioHookResult.Success, status == EmulationStatus.Success);
		}
	}
}

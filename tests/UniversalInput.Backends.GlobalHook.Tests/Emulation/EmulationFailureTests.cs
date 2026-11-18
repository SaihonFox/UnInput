using UniversalInput.Backends.GlobalHook.Emulation;

namespace UniversalInput.Backends.GlobalHook.Tests.Emulation;

public class EmulationFailureTests
{
	[Fact]
	public void EveryFailureStatus_HasAnExceptionThatNamesTheAction()
	{
		foreach (var status in Enum.GetValues<EmulationStatus>().Where(s => s != EmulationStatus.Success))
		{
			var error = EmulationFailure.ToException(status, "нажатие клавиши 0x04");

			Assert.Contains("нажатие клавиши 0x04", error.Message);
		}
	}

	[Fact]
	public void PermissionStatuses_AreDistinguishedByPlatformInTheRemediation()
	{
		var mac = EmulationFailure.ToException(EmulationStatus.AccessibilityDenied, "x");
		var linux = EmulationFailure.ToException(EmulationStatus.UinputUnavailable, "x");

		Assert.Contains("Универсальный доступ", mac.Remediation);
		Assert.Contains("uinput", linux.Remediation);
	}
}

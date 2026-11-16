using System.Runtime.InteropServices;
using UniversalInput.Backends.GlobalHook.Probe;

namespace UniversalInput.Backends.GlobalHook.Tests.Probe;

public class GlobalHookProbeTests
{
	[Theory]
	[InlineData(HookPlatform.Windows, Architecture.X64)]
	[InlineData(HookPlatform.Windows, Architecture.X86)]
	[InlineData(HookPlatform.Windows, Architecture.Arm64)]
	[InlineData(HookPlatform.MacOS, Architecture.X64)]
	[InlineData(HookPlatform.MacOS, Architecture.Arm64)]
	[InlineData(HookPlatform.Linux, Architecture.X64)]
	[InlineData(HookPlatform.Linux, Architecture.Arm)]
	[InlineData(HookPlatform.Linux, Architecture.Arm64)]
	public void SupportedCombinations_AreAvailable(HookPlatform platform, Architecture architecture)
	{
		Assert.True(GlobalHookProbe.Evaluate(platform, architecture, linuxSessionPresent: true).IsAvailable);
	}

	[Theory]
	[InlineData(HookPlatform.Windows, Architecture.Arm)]
	[InlineData(HookPlatform.MacOS, Architecture.X86)]
	[InlineData(HookPlatform.Linux, Architecture.X86)]
	public void UnsupportedArchitectures_AreRestrictedByPlatform(HookPlatform platform, Architecture architecture)
	{
		var probe = GlobalHookProbe.Evaluate(platform, architecture, linuxSessionPresent: true);

		Assert.False(probe.IsAvailable);
		Assert.Equal(InputErrorCode.PlatformRestriction, probe.FailureCode);
	}

	[Fact]
	public void UnknownPlatforms_AreRestrictedByPlatform()
	{
		var probe = GlobalHookProbe.Evaluate(HookPlatform.Other, Architecture.X64, linuxSessionPresent: true);

		Assert.False(probe.IsAvailable);
		Assert.Equal(InputErrorCode.PlatformRestriction, probe.FailureCode);
	}

	[Fact]
	public void Linux_WithoutAGraphicalSession_IsUnavailableWithAHint()
	{
		var probe = GlobalHookProbe.Evaluate(HookPlatform.Linux, Architecture.X64, linuxSessionPresent: false);

		Assert.False(probe.IsAvailable);
		Assert.Equal(InputErrorCode.BackendUnavailable, probe.FailureCode);
		Assert.False(string.IsNullOrEmpty(probe.Remediation));
	}

	[Fact]
	public void TheSessionCheck_AppliesOnlyToLinux()
	{
		Assert.True(GlobalHookProbe.Evaluate(HookPlatform.Windows, Architecture.X64, linuxSessionPresent: false).IsAvailable);
		Assert.True(GlobalHookProbe.Evaluate(HookPlatform.MacOS, Architecture.Arm64, linuxSessionPresent: false).IsAvailable);
	}
}

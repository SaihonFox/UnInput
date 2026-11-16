using System.Runtime.InteropServices;
using UniversalInput.Backends.GlobalHook.Probe;

namespace UniversalInput.Backends.GlobalHook.Tests.Probe;

public class GlobalHookProbeTests
{
	// HookPlatform внутренний, а методы теста публичные, поэтому платформа передаётся именем.
	private static HookPlatform Parse(string name) => Enum.Parse<HookPlatform>(name);

	[Theory]
	[InlineData("Windows", Architecture.X64)]
	[InlineData("Windows", Architecture.X86)]
	[InlineData("Windows", Architecture.Arm64)]
	[InlineData("MacOS", Architecture.X64)]
	[InlineData("MacOS", Architecture.Arm64)]
	[InlineData("Linux", Architecture.X64)]
	[InlineData("Linux", Architecture.Arm)]
	[InlineData("Linux", Architecture.Arm64)]
	public void SupportedCombinations_AreAvailable(string platformName, Architecture architecture)
	{
		Assert.True(GlobalHookProbe.Evaluate(Parse(platformName), architecture, linuxSessionPresent: true).IsAvailable);
	}

	[Theory]
	[InlineData("Windows", Architecture.Arm)]
	[InlineData("MacOS", Architecture.X86)]
	[InlineData("Linux", Architecture.X86)]
	public void UnsupportedArchitectures_AreRestrictedByPlatform(string platformName, Architecture architecture)
	{
		var probe = GlobalHookProbe.Evaluate(Parse(platformName), architecture, linuxSessionPresent: true);

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

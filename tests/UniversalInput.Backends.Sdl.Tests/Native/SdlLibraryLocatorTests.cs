using UniversalInput.Backends.Sdl.Native;

namespace UniversalInput.Backends.Sdl.Tests.Native;

public class SdlLibraryLocatorTests
{
	// SdlPlatform внутренний, а методы теста публичные, поэтому платформа передаётся именем.
	private static SdlPlatform Parse(string name) => Enum.Parse<SdlPlatform>(name);

	[Theory]
	[InlineData("Windows", "SDL3.dll")]
	[InlineData("Linux", "libSDL3.so.0")]
	[InlineData("MacOS", "libSDL3.0.dylib")]
	public void StandardNames_ComeFromThePlatform(string platform, string first)
	{
		var candidates = SdlLibraryLocator.Candidates(Parse(platform), null);

		Assert.Equal(first, candidates[0]);
	}

	[Theory]
	[InlineData("Windows")]
	[InlineData("Linux")]
	[InlineData("MacOS")]
	[InlineData("Other")]
	public void ACustomPath_IsTriedFirst(string platform)
	{
		var candidates = SdlLibraryLocator.Candidates(Parse(platform), "/opt/sdl/libSDL3.so");

		Assert.Equal("/opt/sdl/libSDL3.so", candidates[0]);
		Assert.True(candidates.Count > 1);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("  ")]
	public void ABlankCustomPath_IsIgnored(string? custom)
	{
		var withCustom = SdlLibraryLocator.Candidates(SdlPlatform.Linux, custom);
		var without = SdlLibraryLocator.Candidates(SdlPlatform.Linux, null);

		Assert.Equal<string>(without, withCustom);
	}

	[Theory]
	[InlineData("Windows")]
	[InlineData("Linux")]
	[InlineData("MacOS")]
	[InlineData("Other")]
	public void EveryPlatform_HasARemediation(string platform)
	{
		Assert.False(string.IsNullOrWhiteSpace(SdlLibraryLocator.Remediation(Parse(platform))));
	}

	[Fact]
	public void NotFound_ListsWhatWasTried_AndSaysHowToFixIt()
	{
		var error = SdlLibraryLocator.NotFound(SdlPlatform.Windows, ["a.dll", "b.dll"]);

		Assert.Equal(InputErrorCode.BackendUnavailable, error.Code);
		Assert.Contains("a.dll", error.Message);
		Assert.Contains("b.dll", error.Message);
		Assert.False(string.IsNullOrEmpty(error.Remediation));
	}

	[Fact]
	public void Failures_AreMappedToLibraryErrors()
	{
		var missing = SdlFailure.ToException(new DllNotFoundException("SDL3"));
		var broken = SdlFailure.ToException(new BadImageFormatException("x64 vs arm64"));
		var other = SdlFailure.ToException(new InvalidOperationException("boom"));
		var passthrough = new InputException(InputErrorCode.PermissionDenied, "нет прав");

		Assert.Equal(InputErrorCode.BackendUnavailable, missing.Code);
		Assert.Equal(InputErrorCode.BackendUnavailable, broken.Code);
		Assert.False(string.IsNullOrEmpty(missing.Remediation));
		Assert.Equal(InputErrorCode.BackendFault, other.Code);
		Assert.Contains("boom", other.Message);
		Assert.Same(passthrough, SdlFailure.ToException(passthrough));
	}
}

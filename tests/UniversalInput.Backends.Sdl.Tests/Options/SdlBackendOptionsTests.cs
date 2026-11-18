using UniversalInput.Backends.Sdl.Options;

namespace UniversalInput.Backends.Sdl.Tests.Options;

public class SdlBackendOptionsTests
{
	[Fact]
	public void Defaults_AreValid_AndAllowBackgroundEvents()
	{
		var options = new SdlBackendOptions();

		options.Validate();

		Assert.True(options.AllowBackgroundEvents);
		Assert.False(options.EnableSensors);
		Assert.Null(options.LibraryPath);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(6000)]
	public void APollIntervalOutsideTheRange_IsRejected(int milliseconds)
	{
		var options = new SdlBackendOptions { PollInterval = TimeSpan.FromMilliseconds(milliseconds) };

		Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
	}
}

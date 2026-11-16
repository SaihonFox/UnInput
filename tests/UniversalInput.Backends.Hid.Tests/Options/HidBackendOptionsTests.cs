using UniversalInput.Backends.Hid.Options;

namespace UniversalInput.Backends.Hid.Tests.Options;

public class HidBackendOptionsTests
{
	[Fact]
	public void Defaults_AreValid() => new HidBackendOptions().Validate();

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public void NonPositiveRescanInterval_IsRejected(int seconds)
	{
		var options = new HidBackendOptions { RescanInterval = TimeSpan.FromSeconds(seconds) };

		Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
	}
}

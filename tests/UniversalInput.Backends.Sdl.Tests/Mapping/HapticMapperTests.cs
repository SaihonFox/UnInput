using UniversalInput.Backends.Sdl.Mapping;

namespace UniversalInput.Backends.Sdl.Tests.Mapping;

public class HapticMapperTests
{
	[Fact]
	public void Intensities_ScaleToSixteenBits()
	{
		var (low, high, milliseconds) = HapticMapper.ToRumble(new HapticEffect(1f, 0f, TimeSpan.FromMilliseconds(250)));

		Assert.Equal(ushort.MaxValue, low);
		Assert.Equal(0, high);
		Assert.Equal(250u, milliseconds);
	}

	[Fact]
	public void TheMiddleOfTheRange_RoundsToTheNearestEven()
	{
		var (low, _, _) = HapticMapper.ToRumble(new HapticEffect(0.5f, 0f, TimeSpan.FromSeconds(1)));

		Assert.Equal(32768, low);
	}

	[Fact]
	public void FractionsOfAMillisecond_RoundUp_SoAShortEffectIsNotLost()
	{
		var (_, _, milliseconds) = HapticMapper.ToRumble(new HapticEffect(1f, 1f, TimeSpan.FromTicks(5)));

		Assert.Equal(1u, milliseconds);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-100)]
	public void ANonPositiveDuration_StopsTheVibration(int milliseconds)
	{
		var result = HapticMapper.ToRumble(new HapticEffect(1f, 1f, TimeSpan.FromMilliseconds(milliseconds)));

		Assert.Equal((0, 0, 0u), ((int)result.Low, (int)result.High, result.Milliseconds));
	}

	[Fact]
	public void AVeryLongDuration_IsClamped()
	{
		var (_, _, milliseconds) = HapticMapper.ToRumble(new HapticEffect(1f, 1f, TimeSpan.FromDays(365)));

		Assert.Equal(uint.MaxValue, milliseconds);
	}

	[Theory]
	[InlineData(-0.1f, 0f)]
	[InlineData(0f, 1.1f)]
	[InlineData(float.NaN, 0f)]
	[InlineData(0f, float.PositiveInfinity)]
	public void IntensitiesOutsideZeroToOne_AreRejected(float low, float high)
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => HapticMapper.ToRumble(new HapticEffect(low, high, TimeSpan.FromSeconds(1))));
	}
}

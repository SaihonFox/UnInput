using UniversalInput.Backends.Sdl.Mapping;

namespace UniversalInput.Backends.Sdl.Tests.Mapping;

public class GamepadMapperTests
{
	[Theory]
	[InlineData(0, GamepadButton.South)]
	[InlineData(1, GamepadButton.East)]
	[InlineData(2, GamepadButton.West)]
	[InlineData(3, GamepadButton.North)]
	[InlineData(4, GamepadButton.Back)]
	[InlineData(5, GamepadButton.Guide)]
	[InlineData(6, GamepadButton.Start)]
	[InlineData(7, GamepadButton.LeftStick)]
	[InlineData(8, GamepadButton.RightStick)]
	[InlineData(9, GamepadButton.LeftShoulder)]
	[InlineData(10, GamepadButton.RightShoulder)]
	[InlineData(11, GamepadButton.DPadUp)]
	[InlineData(12, GamepadButton.DPadDown)]
	[InlineData(13, GamepadButton.DPadLeft)]
	[InlineData(14, GamepadButton.DPadRight)]
	public void Buttons_FollowTheSdlNumbering(int sdl, GamepadButton expected)
	{
		Assert.Equal(expected, GamepadMapper.Button(sdl));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(15)]
	[InlineData(20)]
	[InlineData(255)]
	public void ExtraButtons_AreNotDelivered(int sdl)
	{
		Assert.Null(GamepadMapper.Button(sdl));
	}

	[Theory]
	[InlineData(0, GamepadAxis.LeftX)]
	[InlineData(1, GamepadAxis.LeftY)]
	[InlineData(2, GamepadAxis.RightX)]
	[InlineData(3, GamepadAxis.RightY)]
	[InlineData(4, GamepadAxis.LeftTrigger)]
	[InlineData(5, GamepadAxis.RightTrigger)]
	public void Axes_FollowTheSdlNumbering(int sdl, GamepadAxis expected)
	{
		Assert.Equal(expected, GamepadMapper.Axis(sdl));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(6)]
	public void UnknownAxes_AreNotDelivered(int sdl)
	{
		Assert.Null(GamepadMapper.Axis(sdl));
	}

	[Theory]
	[InlineData(GamepadAxis.LeftX, short.MinValue, -1f)]
	[InlineData(GamepadAxis.LeftX, short.MaxValue, 1f)]
	[InlineData(GamepadAxis.RightY, 0, 0f)]
	[InlineData(GamepadAxis.LeftTrigger, 0, 0f)]
	[InlineData(GamepadAxis.RightTrigger, short.MaxValue, 1f)]
	[InlineData(GamepadAxis.LeftTrigger, -500, 0f)]
	public void Normalize_ScalesToTheRangeOfTheAxis(GamepadAxis axis, short raw, float expected)
	{
		Assert.Equal(expected, GamepadMapper.Normalize(axis, raw), 0.0001f);
	}

	[Fact]
	public void Normalize_KeepsTheSdlDirection_UpIsNegative()
	{
		Assert.True(GamepadMapper.Normalize(GamepadAxis.LeftY, -20000) < 0);
	}

	[Theory]
	[InlineData(1, SensorKind.Accelerometer)]
	[InlineData(2, SensorKind.Gyroscope)]
	public void Sensors_AreTranslated(int sdl, SensorKind expected)
	{
		Assert.Equal(expected, GamepadMapper.Sensor(sdl));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(0)]
	[InlineData(3)]
	[InlineData(6)]
	public void OtherSensors_AreNotDelivered(int sdl)
	{
		Assert.Null(GamepadMapper.Sensor(sdl));
	}
}

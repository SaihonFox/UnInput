using System.Buffers.Binary;
using UniversalInput.Backends.Sdl.Runtime;

namespace UniversalInput.Backends.Sdl.Tests.Runtime;

public class SdlEventParserTests
{
	private static byte[] Raw(uint type, uint which)
	{
		var raw = new byte[SdlEventParser.EventSize];
		BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0), type);
		BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(8), 123456789);
		BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(16), which);
		return raw;
	}

	[Fact]
	public void Added_AndRemoved_CarryTheInstanceId()
	{
		Assert.Equal(SdlEvent.Added(7), SdlEventParser.Parse(Raw(SdlEventParser.TypeAdded, 7)));
		Assert.Equal(SdlEvent.Removed(9), SdlEventParser.Parse(Raw(SdlEventParser.TypeRemoved, 9)));
	}

	[Fact]
	public void ButtonEvents_CarryTheButtonNumber_AndTheDirectionComesFromTheType()
	{
		var down = Raw(SdlEventParser.TypeButtonDown, 3);
		down[20] = 4;
		down[21] = 1;
		var up = Raw(SdlEventParser.TypeButtonUp, 3);
		up[20] = 4;

		Assert.Equal(SdlEvent.ButtonDown(3, 4), SdlEventParser.Parse(down));
		Assert.Equal(SdlEvent.ButtonUp(3, 4), SdlEventParser.Parse(up));
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(2, 32767)]
	[InlineData(1, -32768)]
	[InlineData(5, -1234)]
	public void AxisMotion_ReadsTheSignedValueAfterThePadding(int axis, short value)
	{
		var raw = Raw(SdlEventParser.TypeAxisMotion, 5);
		raw[20] = (byte)axis;
		BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(24), value);

		Assert.Equal(SdlEvent.AxisMotion(5, axis, value), SdlEventParser.Parse(raw));
	}

	[Fact]
	public void SensorUpdate_ReadsTheTypeAndThreeValues()
	{
		var raw = Raw(SdlEventParser.TypeSensorUpdate, 2);
		BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(20), 2);
		BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(24), 0.5f);
		BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(28), -1.5f);
		BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(32), 9.8f);

		Assert.Equal(SdlEvent.SensorUpdate(2, 2, 0.5f, -1.5f, 9.8f), SdlEventParser.Parse(raw));
	}

	[Fact]
	public void UnknownTypes_BecomeOther()
	{
		Assert.Equal(SdlEventKind.Other, SdlEventParser.Parse(Raw(0x100, 1)).Kind);
		Assert.Equal(SdlEventKind.Other, SdlEventParser.Parse(Raw(0x600, 1)).Kind);
	}

	[Fact]
	public void TruncatedInput_DoesNotThrow()
	{
		Assert.Equal(SdlEventKind.Other, SdlEventParser.Parse([]).Kind);
		Assert.Equal(SdlEventKind.Other, SdlEventParser.Parse(Raw(SdlEventParser.TypeAxisMotion, 1).AsSpan(0, 22)).Kind);
		Assert.Equal(SdlEventKind.Other, SdlEventParser.Parse(Raw(SdlEventParser.TypeSensorUpdate, 1).AsSpan(0, 30)).Kind);
	}

	[Fact]
	public void TheEventTypeNumbers_MatchSdl3()
	{
		Assert.Equal(0x650u, SdlEventParser.TypeAxisMotion);
		Assert.Equal(0x651u, SdlEventParser.TypeButtonDown);
		Assert.Equal(0x652u, SdlEventParser.TypeButtonUp);
		Assert.Equal(0x653u, SdlEventParser.TypeAdded);
		Assert.Equal(0x654u, SdlEventParser.TypeRemoved);
		Assert.Equal(0x659u, SdlEventParser.TypeSensorUpdate);
	}
}

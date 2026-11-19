using UniversalInput.Backends.Hid.Descriptors;
using UniversalInput.Backends.Hid.Tests.Fakes;

namespace UniversalInput.Backends.Hid.Tests.Descriptors;

public class ReportLayoutReaderTests
{
	[Fact]
	public void BootKeyboard_HasModifierBits_AndASixKeyArray_AndSkipsConstantAndOutputFields()
	{
		var fields = ReportLayoutReader.ReadInputFields(HidTestDescriptors.BootKeyboard);

		Assert.Equal(2, fields.Count);

		var modifiers = fields[0];
		Assert.True(modifiers.IsVariable);
		Assert.Equal(0, modifiers.BitOffset);
		Assert.Equal(1, modifiers.BitSize);
		Assert.Equal(8, modifiers.Count);
		Assert.Equal(new HidUsage(0x07, 0xE0), modifiers.Usages[0]);
		Assert.Equal(new HidUsage(0x07, 0xE7), modifiers.Usages[7]);
		Assert.Equal(new HidUsage(0x01, 0x06), modifiers.Application);

		var keys = fields[1];
		Assert.False(keys.IsVariable);
		Assert.Equal(16, keys.BitOffset);
		Assert.Equal(8, keys.BitSize);
		Assert.Equal(6, keys.Count);
		Assert.Equal(new HidUsage(0x07, 0x04), keys.UsageOfArrayValue(4));
		Assert.Null(keys.UsageOfArrayValue(0x66));
	}

	[Fact]
	public void WheelMouse_HasButtons_AndSignedRelativeAxes()
	{
		var fields = ReportLayoutReader.ReadInputFields(HidTestDescriptors.WheelMouse);

		Assert.Equal(2, fields.Count);
		Assert.Equal(new HidUsage(0x01, 0x02), fields[0].Application);
		Assert.Equal(3, fields[0].Count);
		Assert.Equal(new HidUsage(0x09, 0x01), fields[0].Usages[0]);

		var axes = fields[1];
		Assert.Equal(8, axes.BitOffset);
		Assert.Equal(3, axes.Count);
		Assert.True(axes.IsRelative);
		Assert.True(axes.IsSigned);
		Assert.Equal(-127, axes.LogicalMin);
		Assert.Equal(127, axes.LogicalMax);
		Assert.Equal<HidUsage>([new(0x01, 0x30), new(0x01, 0x31), new(0x01, 0x38)], axes.Usages);
	}

	[Fact]
	public void ReportIds_HaveTheirOwnOffsets()
	{
		var fields = ReportLayoutReader.ReadInputFields(HidTestDescriptors.NumberedKeyboard);

		Assert.Equal<int>([1, 1, 2], fields.Select(f => f.ReportId));
		Assert.Equal(0, fields[0].BitOffset);
		Assert.Equal(8, fields[1].BitOffset);
		Assert.Equal(0, fields[2].BitOffset);
		Assert.Equal(new HidUsage(0xFF00, 0x01), fields[2].Usages[0]);
	}

	[Fact]
	public void PushAndPop_RestoreTheGlobalState()
	{
		byte[] descriptor =
		[
			0x05, 0x01, 0x09, 0x05, 0xA1, 0x01,
			0x05, 0x09, 0x19, 0x01, 0x29, 0x04, 0x15, 0x00, 0x25, 0x01, 0x75, 0x01, 0x95, 0x04,
			0xA4,
			0x75, 0x08, 0x95, 0x01, 0x05, 0x01, 0x09, 0x30, 0x15, 0x00, 0x26, 0xFF, 0x00, 0x81, 0x02,
			0xB4,
			0x09, 0x31, 0x81, 0x02,
			0xC0,
		];

		var fields = ReportLayoutReader.ReadInputFields(descriptor);

		Assert.Equal(1, fields.Count(f => f.BitSize == 8));
		var restored = fields[^1];
		Assert.Equal(1, restored.BitSize);
		Assert.Equal(4, restored.Count);
	}

	[Fact]
	public void AnUnsignedMaximum_IsNotReadAsNegative()
	{
		byte[] descriptor = [0x05, 0x01, 0x09, 0x05, 0xA1, 0x01, 0x09, 0x30, 0x15, 0x00, 0x26, 0xFF, 0x00, 0x75, 0x10, 0x95, 0x01, 0x81, 0x02, 0xC0];

		var field = Assert.Single(ReportLayoutReader.ReadInputFields(descriptor));

		Assert.Equal(0xFF, field.LogicalMax);
		Assert.False(field.IsSigned);
	}

	[Fact]
	public void ATruncatedDescriptor_ReturnsWhatWasRead_WithoutThrowing()
	{
		var truncated = HidTestDescriptors.BootKeyboard[..^8];

		var fields = ReportLayoutReader.ReadInputFields(truncated);

		Assert.NotEmpty(fields);
		Assert.Empty(ReportLayoutReader.ReadInputFields([]));
	}
}

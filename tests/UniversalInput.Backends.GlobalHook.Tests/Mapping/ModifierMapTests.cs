using SharpHook.Data;
using UniversalInput.Backends.GlobalHook.Mapping;

namespace UniversalInput.Backends.GlobalHook.Tests.Mapping;

public class ModifierMapTests
{
	[Theory]
	[InlineData(EventMask.LeftShift, KeyModifiers.Shift)]
	[InlineData(EventMask.RightShift, KeyModifiers.Shift)]
	[InlineData(EventMask.LeftCtrl, KeyModifiers.Control)]
	[InlineData(EventMask.RightCtrl, KeyModifiers.Control)]
	[InlineData(EventMask.LeftAlt, KeyModifiers.Alt)]
	[InlineData(EventMask.RightAlt, KeyModifiers.Alt)]
	[InlineData(EventMask.LeftMeta, KeyModifiers.Meta)]
	[InlineData(EventMask.RightMeta, KeyModifiers.Meta)]
	[InlineData(EventMask.CapsLock, KeyModifiers.CapsLock)]
	[InlineData(EventMask.NumLock, KeyModifiers.NumLock)]
	[InlineData(EventMask.None, KeyModifiers.None)]
	public void SingleFlags_AreTranslated(EventMask mask, KeyModifiers expected)
	{
		Assert.Equal(expected, ModifierMap.From(mask));
	}

	[Fact]
	public void Combinations_AreTranslatedTogether()
	{
		var result = ModifierMap.From(EventMask.LeftCtrl | EventMask.RightShift | EventMask.CapsLock);

		Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.CapsLock, result);
	}

	[Fact]
	public void MouseButtons_DoNotLeakIntoModifiers()
	{
		var result = ModifierMap.From(EventMask.Button1 | EventMask.Button3);

		Assert.Equal(KeyModifiers.None, result);
	}
}

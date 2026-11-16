using SharpHook.Data;
using UniversalInput.Backends.GlobalHook.Mapping;

namespace UniversalInput.Backends.GlobalHook.Tests.Mapping;

public class KeyMapTests
{
	[Fact]
	public void EveryKeyCode_IsEitherMappedOrExplicitlyUnmapped()
	{
		foreach (var code in Enum.GetValues<KeyCode>())
		{
			var mapped = KeyMap.TryGetUsage(code, out _);
			var unmapped = KeyMap.Unmapped.Contains(code);

			Assert.True(mapped ^ unmapped, $"{code}: должен быть ровно в одном из списков (переведён: {mapped}, в Unmapped: {unmapped}).");
		}
	}

	[Fact]
	public void Usages_AreUnique_AndLieOnTheKeyboardPage()
	{
		var usages = KeyMap.Mapped.Select(code =>
		{
			KeyMap.TryGetUsage(code, out var usage);
			return usage;
		}).ToList();

		Assert.Equal(usages.Count, usages.Distinct().Count());
		Assert.All(usages, usage => Assert.InRange(usage, 0x04, 0xE7));
	}

	[Theory]
	[InlineData(KeyCode.VcA, KeyCodes.A)]
	[InlineData(KeyCode.VcZ, KeyCodes.Z)]
	[InlineData(KeyCode.Vc1, KeyCodes.Digit1)]
	[InlineData(KeyCode.Vc0, KeyCodes.Digit0)]
	[InlineData(KeyCode.VcEnter, KeyCodes.Enter)]
	[InlineData(KeyCode.VcEscape, KeyCodes.Escape)]
	[InlineData(KeyCode.VcBackspace, KeyCodes.Backspace)]
	[InlineData(KeyCode.VcTab, KeyCodes.Tab)]
	[InlineData(KeyCode.VcSpace, KeyCodes.Space)]
	[InlineData(KeyCode.VcLeftControl, KeyCodes.LeftControl)]
	[InlineData(KeyCode.VcLeftShift, KeyCodes.LeftShift)]
	[InlineData(KeyCode.VcLeftAlt, KeyCodes.LeftAlt)]
	[InlineData(KeyCode.VcLeftMeta, KeyCodes.LeftMeta)]
	[InlineData(KeyCode.VcF1, 0x3A)]
	[InlineData(KeyCode.VcF12, 0x45)]
	[InlineData(KeyCode.VcF13, 0x68)]
	[InlineData(KeyCode.VcUp, 0x52)]
	[InlineData(KeyCode.VcNumPad0, 0x62)]
	[InlineData(KeyCode.VcNumPad1, 0x59)]
	[InlineData(KeyCode.VcRightMeta, 0xE7)]
	public void KnownKeys_MapToTheirUsbHidUsage(KeyCode code, int expected)
	{
		Assert.True(KeyMap.TryGetUsage(code, out var usage));
		Assert.Equal(expected, usage);
	}

	[Theory]
	[InlineData(KeyCode.VcVolumeUp)]
	[InlineData(KeyCode.VcMediaPlay)]
	[InlineData(KeyCode.VcBrowserBack)]
	[InlineData(KeyCode.VcUndefined)]
	public void KeysWithoutAKeyboardPageCode_AreNotInvented(KeyCode code)
	{
		Assert.False(KeyMap.TryGetUsage(code, out _));
	}
}

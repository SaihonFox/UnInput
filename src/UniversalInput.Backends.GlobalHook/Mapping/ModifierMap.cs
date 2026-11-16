using SharpHook.Data;

namespace UniversalInput.Backends.GlobalHook.Mapping;

internal static class ModifierMap
{
	/// <summary>Левые и правые клавиши сливаются в одну: контракт <see cref="KeyModifiers"/> не различает сторону.</summary>
	public static KeyModifiers From(EventMask mask)
	{
		var result = KeyModifiers.None;
		if ((mask & EventMask.Shift) != 0)
		{
			result |= KeyModifiers.Shift;
		}

		if ((mask & EventMask.Ctrl) != 0)
		{
			result |= KeyModifiers.Control;
		}

		if ((mask & EventMask.Alt) != 0)
		{
			result |= KeyModifiers.Alt;
		}

		if ((mask & EventMask.Meta) != 0)
		{
			result |= KeyModifiers.Meta;
		}

		if ((mask & EventMask.CapsLock) != 0)
		{
			result |= KeyModifiers.CapsLock;
		}

		if ((mask & EventMask.NumLock) != 0)
		{
			result |= KeyModifiers.NumLock;
		}

		return result;
	}
}

namespace UniversalInput.Backends.Hid.Descriptors;

/// <summary>Пара «страница применения — применение» из таблиц HID Usage Tables (например, 0x0001:0x0006 — клавиатура).</summary>
public readonly record struct HidUsage(int Page, int Usage)
{
	public override string ToString() => $"{Page:X4}:{Usage:X4}";
}

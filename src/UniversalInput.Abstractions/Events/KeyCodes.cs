namespace UniversalInput.Abstractions.Events;

/// <summary>
/// Канонические коды клавиш: USB HID Usage, страница клавиатуры (0x07).
/// Здесь только часто используемые; backend обязан переводить свои коды в эту таблицу.
/// </summary>
public static class KeyCodes
{
	public const int A = 0x04;
	public const int B = 0x05;
	public const int C = 0x06;
	public const int D = 0x07;
	public const int E = 0x08;
	public const int F = 0x09;
	public const int Q = 0x14;
	public const int W = 0x1A;
	public const int Z = 0x1D;
	public const int Digit1 = 0x1E;
	public const int Digit0 = 0x27;
	public const int Enter = 0x28;
	public const int Escape = 0x29;
	public const int Backspace = 0x2A;
	public const int Tab = 0x2B;
	public const int Space = 0x2C;
	public const int LeftControl = 0xE0;
	public const int LeftShift = 0xE1;
	public const int LeftAlt = 0xE2;
	public const int LeftMeta = 0xE3;
}

namespace UniversalInput.Abstractions.Events.Views;

/// <summary>Клавиша: USB HID Usage, символ Unicode (0, если backend его не знает), модификаторы.</summary>
public readonly record struct KeyEvent(int Usage, int Codepoint, KeyModifiers Modifiers, bool IsRepeat);

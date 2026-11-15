namespace UniversalInput.Abstractions.Events.Views;

/// <summary>Движение указателя: смещение или абсолютные координаты.</summary>
public readonly record struct PointerEvent(float X, float Y, bool IsAbsolute);

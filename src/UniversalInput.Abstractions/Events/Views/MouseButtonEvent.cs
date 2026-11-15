namespace UniversalInput.Abstractions.Events.Views;

/// <summary>Кнопка мыши (см. <see cref="MouseButton"/>) и координаты, если известны.</summary>
public readonly record struct MouseButtonEvent(int Button, float X, float Y);

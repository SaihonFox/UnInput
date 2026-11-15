namespace UniversalInput.Abstractions.Events.Views;

/// <summary>Касание: идентификатор, координаты и давление.</summary>
public readonly record struct TouchEvent(int TouchId, float X, float Y, float Pressure);

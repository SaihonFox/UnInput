namespace UniversalInput.Abstractions.Events.Views;

/// <summary>Смещение колеса по осям.</summary>
public readonly record struct WheelEvent(float DeltaX, float DeltaY);

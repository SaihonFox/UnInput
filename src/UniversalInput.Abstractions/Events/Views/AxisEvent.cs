namespace UniversalInput.Abstractions.Events.Views;

/// <summary>Ось (см. <see cref="GamepadAxis"/> для геймпадов) и значение.</summary>
public readonly record struct AxisEvent(int Axis, float Value);

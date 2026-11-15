namespace UniversalInput.Abstractions.Events.Views;

/// <summary>Показания датчика по трём осям.</summary>
public readonly record struct SensorEvent(SensorKind Sensor, float X, float Y, float Z);

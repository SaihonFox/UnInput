namespace UniversalInput.Abstractions.Features;

/// <summary>Простой эффект вибрации: мощность низкочастотного и высокочастотного моторов (0..1) и длительность.</summary>
public readonly record struct HapticEffect(float LowFrequency, float HighFrequency, TimeSpan Duration);

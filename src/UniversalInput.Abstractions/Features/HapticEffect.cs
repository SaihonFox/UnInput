namespace UniversalInput.Abstractions.Features;

/// <summary>Простой эффект вибрации: мощность низкочастотного и высокочастотного моторов (0..1) и длительность.</summary>
/// <param name="LowFrequency">Сила низкочастотного (тяжёлого) мотора от 0 до 1.</param>
/// <param name="HighFrequency">Сила высокочастотного (лёгкого) мотора от 0 до 1.</param>
/// <param name="Duration">Длительность; нулевая или отрицательная останавливает вибрацию.</param>
public readonly record struct HapticEffect(float LowFrequency, float HighFrequency, TimeSpan Duration);

namespace UniversalInput.Abstractions.Events.Views;

/// <summary>Пропуск в потоке: сколько событий потеряно и почему.</summary>
public readonly record struct StreamGapEvent(int Lost, StreamGapReason Reason);

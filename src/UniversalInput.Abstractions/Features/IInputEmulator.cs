namespace UniversalInput.Abstractions.Features;

/// <summary>Эмуляция ввода в ОС или на устройстве.</summary>
public interface IInputEmulator : IBackendFeature
{
	ValueTask EmulateAsync(InputEvent inputEvent, CancellationToken cancellationToken);
}

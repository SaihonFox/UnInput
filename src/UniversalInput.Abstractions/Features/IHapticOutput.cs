namespace UniversalInput.Abstractions.Features;

public interface IHapticOutput : IBackendFeature
{
	ValueTask PlayAsync(DeviceId id, HapticEffect effect, CancellationToken cancellationToken);
}

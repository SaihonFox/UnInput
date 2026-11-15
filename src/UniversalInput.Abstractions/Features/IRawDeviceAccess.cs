namespace UniversalInput.Abstractions.Features;

/// <summary>Прямой доступ к устройству (raw reports) отдельным каналом, вне общего потока событий.</summary>
public interface IRawDeviceAccess : IBackendFeature
{
	ValueTask<IRawDeviceChannel> OpenAsync(DeviceId id, RawAccessMode mode, CancellationToken cancellationToken);
}

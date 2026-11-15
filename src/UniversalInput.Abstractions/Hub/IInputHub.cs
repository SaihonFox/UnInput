namespace UniversalInput.Abstractions.Hub;

/// <summary>Единственный вход приложения. Потокобезопасен.</summary>
public interface IInputHub
{
	IReadOnlyList<InputDevice> Devices { get; }

	IReadOnlyList<BackendStatus> Backends { get; }

	IInputSubscription Subscribe(SubscriptionOptions? options = null);

	IAsyncEnumerable<InputEvent> ReadEventsAsync(
		SubscriptionOptions? options = null, CancellationToken cancellationToken = default);

	/// <summary>Проверяет операцию без исключений и побочных эффектов.</summary>
	OperationSupport GetSupport(InputOperation operation, DeviceId? device = null);

	/// <exception cref="InputException">Эмуляция недоступна.</exception>
	ValueTask EmulateAsync(InputEvent inputEvent, CancellationToken cancellationToken = default);

	/// <exception cref="InputException">Устройство недоступно, занято или не поддерживает прямой доступ.</exception>
	ValueTask<IRawDeviceChannel> OpenRawAsync(
		DeviceId id, RawAccessMode mode, CancellationToken cancellationToken = default);
}

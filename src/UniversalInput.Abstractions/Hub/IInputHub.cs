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

	/// <summary>
	/// Задаёт правило подавления событий через backend, который владеет ролью <see cref="InputOperation.Intercept"/>
	/// (для устройства или, если оно не указано, через первый подходящий по приоритету); <c>null</c> отключает подавление.
	/// Подавлённое событие не доходит до других программ, но подписчикам хаба всё равно доставляется.
	/// </summary>
	/// <exception cref="InputException">Подавление недоступно (платформа или backend его не поддерживает).</exception>
	void SetInterceptRule(InterceptRule? rule, DeviceId device = default);

	/// <exception cref="InputException">Устройство недоступно, занято или не поддерживает прямой доступ.</exception>
	/// <summary>
	/// Запускает вибрацию на устройстве через backend, который владеет ролью <see cref="InputOperation.Output"/> для этого устройства.
	/// Новый вызов заменяет предыдущий эффект. Единицы эффекта описаны в <see cref="HapticEffect"/>.
	/// </summary>
	ValueTask PlayHapticAsync(DeviceId id, HapticEffect effect, CancellationToken cancellationToken = default);

	ValueTask<IRawDeviceChannel> OpenRawAsync(
		DeviceId id, RawAccessMode mode, CancellationToken cancellationToken = default);
}

namespace UniversalInput.Abstractions.Hub;

/// <summary>Параметры подписки: что получать и как вести себя при переполнении.</summary>
public sealed record SubscriptionOptions
{
	/// <summary>Ёмкость канала подписки.</summary>
	public int Capacity { get; init; } = 4096;

	public OverflowPolicy Overflow { get; init; } = OverflowPolicy.DropOldest;

	/// <summary>Типы событий; <c>null</c> — все. <see cref="InputEventKind.StreamGap"/> проходит всегда.</summary>
	public IReadOnlyCollection<InputEventKind>? Kinds { get; init; }

	/// <summary>Конкретные устройства; <c>null</c> — любые.</summary>
	public IReadOnlyCollection<DeviceId>? Devices { get; init; }

	/// <summary>Виды устройств; <c>null</c> — любые.</summary>
	public IReadOnlyCollection<InputDeviceKind>? DeviceKinds { get; init; }

	/// <summary>Идентификаторы backend; <c>null</c> — любые.</summary>
	public IReadOnlyCollection<string>? BackendIds { get; init; }

	/// <summary>Происхождение устройств; <c>null</c> — любое.</summary>
	public IReadOnlyCollection<DeviceOrigin>? Origins { get; init; }

	/// <summary>
	/// Предел частоты непрерывных событий (движение мыши, колесо, оси, касания, датчики), событий в секунду;
	/// <c>null</c> — без предела. Лишние события склеиваются, а не теряются: относительное движение и колесо
	/// суммируются, абсолютные значения заменяются последним. Предел действует на каждый поток отдельно:
	/// устройство + вид события + ось (или касание, или датчик). Дискретные события (клавиши, кнопки) не ограничиваются.
	/// </summary>
	public double? MaxContinuousRate { get; init; }

	/// <summary>
	/// Дополнительный предикат. Выполняется на потоке диспетчера, поэтому должен быть быстрым и не блокировать.
	/// </summary>
	public Func<InputEvent, bool>? Predicate { get; init; }
}

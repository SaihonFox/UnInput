namespace UniversalInput.Abstractions.Hub;

/// <summary>Что делать, когда канал подписки заполнен дискретными событиями.</summary>
public enum OverflowPolicy
{
	/// <summary>Вытеснить самые старые события и добавить <see cref="InputEventKind.StreamGap"/>.</summary>
	DropOldest,

	/// <summary>Отбросить новые события и добавить <see cref="InputEventKind.StreamGap"/>.</summary>
	DropNewest,

	/// <summary>Завершить подписку ошибкой.</summary>
	FaultSubscription,
}

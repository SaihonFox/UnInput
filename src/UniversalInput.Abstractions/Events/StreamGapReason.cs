namespace UniversalInput.Abstractions.Events;

/// <summary>Почему события были потеряны.</summary>
public enum StreamGapReason
{
	/// <summary>Переполнена очередь backend: события отброшены до диспетчера.</summary>
	BackendQueueOverflow,

	/// <summary>Переполнен канал подписки: самые старые события вытеснены.</summary>
	SubscriptionOverflow,

	/// <summary>Переполнен канал подписки: отброшены самые новые события.</summary>
	SubscriptionDroppedNewest,
}

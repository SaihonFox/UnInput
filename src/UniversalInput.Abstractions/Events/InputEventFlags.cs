namespace UniversalInput.Abstractions.Events;

[Flags]
public enum InputEventFlags : ushort
{
	None = 0,

	/// <summary>Событие создано эмуляцией, а не физическим действием.</summary>
	Simulated = 1 << 0,

	/// <summary>Автоповтор клавиши.</summary>
	Repeat = 1 << 1,

	/// <summary>Координаты абсолютные (иначе — относительное смещение).</summary>
	Absolute = 1 << 2,

	/// <summary>Событие получено склейкой нескольких непрерывных событий.</summary>
	Coalesced = 1 << 3,

	/// <summary>Событие пришло от агрегатного источника (глобальный хук).</summary>
	Aggregate = 1 << 4,
}

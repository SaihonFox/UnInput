namespace UniversalInput.Abstractions.Devices;

/// <summary>Откуда взялось устройство.</summary>
public enum DeviceOrigin
{
	/// <summary>Физическое устройство, подключённое к этому компьютеру.</summary>
	Physical,

	/// <summary>Виртуальное устройство, созданное программно.</summary>
	Virtual,

	/// <summary>Устройство на другой машине (например, смартфон по сети).</summary>
	Remote,

	/// <summary>«Системная клавиатура и мышь» от глобального хука без привязки к физическому устройству.</summary>
	Aggregate,
}

using UniversalInput.Backends.Hid.Discovery;

namespace UniversalInput.Backends.Hid.Platform;

/// <summary>Работа ОС с HID: перечисление, уведомления о смене списка и открытие. Выделено, чтобы backend проверялся без железа.</summary>
internal interface IHidPlatform
{
	/// <summary>Блокирующий вызов: перечисление может занимать заметное время.</summary>
	IReadOnlyList<HidDeviceInfo> GetDevices();

	/// <summary>Подписка на смену списка устройств; обработчик вызывается из потока ОС или HidSharp.</summary>
	IDisposable SubscribeChanged(Action handler);

	/// <summary>Блокирующее открытие.</summary>
	IHidConnection Open(string path);
}

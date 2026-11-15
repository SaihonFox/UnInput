namespace UniversalInput.Abstractions.Errors;

/// <summary>Причина ошибки, разделённая по тому, кто может её исправить.</summary>
public enum InputErrorCode
{
	/// <summary>Backend или устройство не умеет операцию. Исправляет приложение: выбирает другой путь.</summary>
	UnsupportedOperation,

	/// <summary>Разрешение можно выдать. Исправляет пользователь или администратор.</summary>
	PermissionDenied,

	/// <summary>ОС не допускает операцию в принципе или в этой конфигурации. Исправить нельзя.</summary>
	PlatformRestriction,

	/// <summary>Устройство отключено или недоступно.</summary>
	DeviceUnavailable,

	/// <summary>Устройство занято другим владельцем.</summary>
	DeviceBusy,

	/// <summary>Backend не запущен или не прошёл проверку.</summary>
	BackendUnavailable,

	/// <summary>Внутренний сбой backend.</summary>
	BackendFault,
}

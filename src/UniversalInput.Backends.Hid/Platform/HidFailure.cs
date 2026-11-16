namespace UniversalInput.Backends.Hid.Platform;

internal static class HidFailure
{
	/// <summary>Приводит сбой перечисления к ошибке библиотеки. Отсутствие нативной библиотеки лечится установкой, а не перезапуском.</summary>
	public static InputException ToException(Exception error) => error switch
	{
		InputException input => input,
		DllNotFoundException or EntryPointNotFoundException or TypeInitializationException { InnerException: DllNotFoundException } =>
			new InputException(
				InputErrorCode.BackendUnavailable,
				"Не найдена системная библиотека для работы с HID: " + error.Message,
				error)
			{
				Remediation = OperatingSystem.IsLinux()
					? "Установите libudev (пакет libudev1)."
					: "Проверьте, что HidSharp поддерживает эту ОС и архитектуру.",
			},
		_ => InputException.BackendFault("Не удалось получить список HID-устройств: " + error.Message, error),
	};
}

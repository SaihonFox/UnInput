namespace UniversalInput.Backends.GlobalHook.Probe;

/// <summary>Перевод ошибки запуска хука в <see cref="InputException"/> с понятной причиной.</summary>
internal static class HookFailure
{
	public static InputException ToException(Exception error) => error switch
	{
		InputException input => input,
		_ when OperatingSystem.IsMacOS() => new InputException(
			InputErrorCode.PermissionDenied,
			"macOS не дал создать глобальный хук (чаще всего нет доступа Accessibility): " + error.Message,
			error)
		{
			Remediation = "Системные настройки → Конфиденциальность и безопасность → Универсальный доступ: включите приложение и перезапустите его.",
		},
		_ when OperatingSystem.IsLinux() => new InputException(
			InputErrorCode.BackendUnavailable,
			"Не удалось включить глобальный хук (нет доступа к X11 или Wayland): " + error.Message,
			error)
		{
			Remediation = "Нужен графический сеанс; на Wayland глобальный хук требует повышенных прав (правило udev для /dev/input и /dev/uinput).",
		},
		_ => InputException.BackendFault("Не удалось включить глобальный хук: " + error.Message, error),
	};
}

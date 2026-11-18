namespace UniversalInput.Backends.GlobalHook.Emulation;

/// <summary>Перевод <see cref="EmulationStatus"/> в <see cref="InputException"/> с понятной причиной и подсказкой, что делать.</summary>
internal static class EmulationFailure
{
	public static InputException ToException(EmulationStatus status, string action) => status switch
	{
		EmulationStatus.Unsupported => InputException.Unsupported(
			$"Эта платформа не умеет эмулировать «{action}» через libuiohook."),
		EmulationStatus.UnknownKey => InputException.Unsupported(
			$"Для «{action}» нет кода клавиши в libuiohook."),
		EmulationStatus.AccessibilityDenied => new InputException(
			InputErrorCode.PermissionDenied,
			$"macOS не разрешила эмуляцию ввода («{action}»): у приложения нет доступа Accessibility.")
		{
			Remediation = "Системные настройки → Конфиденциальность и безопасность → Универсальный доступ: включите приложение и перезапустите его.",
		},
		EmulationStatus.UinputUnavailable => new InputException(
			InputErrorCode.PermissionDenied,
			$"Linux не дал отправить событие («{action}») через uinput.")
		{
			Remediation = "Нужен доступ на запись к /dev/uinput: правило udev и группа пользователя (или запуск с повышенными правами).",
		},
		EmulationStatus.DisplayUnavailable => InputException.BackendUnavailable(
			$"Нет соединения с X11 или Wayland, эмуляция («{action}») невозможна: нужен графический сеанс."),
		_ => InputException.BackendFault($"libuiohook не смог выполнить «{action}»."),
	};
}

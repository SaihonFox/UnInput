namespace UniversalInput.Backends.GlobalHook.Emulation;

/// <summary>Итог одной команды эмуляции. Свои значения нужны, чтобы перевод ошибок в <see cref="InputException"/> не зависел от SharpHook и проверялся тестами.</summary>
internal enum EmulationStatus
{
	Success,

	/// <summary>Неуточнённый сбой библиотеки.</summary>
	Failed,

	/// <summary>Библиотека не умеет такую команду на этой платформе.</summary>
	Unsupported,

	/// <summary>У клавиши нет кода в libuiohook.</summary>
	UnknownKey,

	/// <summary>macOS: нет доступа Accessibility.</summary>
	AccessibilityDenied,

	/// <summary>Linux: не удалось открыть или использовать uinput.</summary>
	UinputUnavailable,

	/// <summary>Linux: нет соединения с X11 или Wayland.</summary>
	DisplayUnavailable,
}

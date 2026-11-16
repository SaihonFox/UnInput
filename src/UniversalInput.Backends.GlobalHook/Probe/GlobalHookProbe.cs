using System.Runtime.InteropServices;

namespace UniversalInput.Backends.GlobalHook.Probe;

internal enum HookPlatform
{
	Windows,
	MacOS,
	Linux,
	Other,
}

/// <summary>
/// Проверка, возможен ли хук в принципе: платформа, архитектура, графический сеанс. Разрешения (macOS Accessibility,
/// права на Wayland) заранее не проверить: они выясняются при запуске и отражаются в ошибке <see cref="BackendProbe"/>/запуска.
/// </summary>
internal static class GlobalHookProbe
{
	public static BackendProbe ForCurrentSystem() => Evaluate(
		CurrentPlatform(),
		RuntimeInformation.ProcessArchitecture,
		!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
			|| !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")));

	public static BackendProbe Evaluate(HookPlatform platform, Architecture architecture, bool linuxSessionPresent)
	{
		var supportedArchitecture = platform switch
		{
			HookPlatform.Windows => architecture != Architecture.Arm,
			HookPlatform.MacOS => architecture is Architecture.X64 or Architecture.Arm64,
			HookPlatform.Linux => architecture != Architecture.X86,
			_ => false,
		};

		if (platform == HookPlatform.Other)
		{
			return BackendProbe.Unavailable(
				InputErrorCode.PlatformRestriction,
				"Глобальный хук (SharpHook) поддерживает только Windows, macOS и Linux; на мобильных платформах и в браузере системный ввод недоступен приложениям.");
		}

		if (!supportedArchitecture)
		{
			return BackendProbe.Unavailable(
				InputErrorCode.PlatformRestriction,
				$"SharpHook не поддерживает архитектуру {architecture} на этой ОС.");
		}

		if (platform == HookPlatform.Linux && !linuxSessionPresent)
		{
			return BackendProbe.Unavailable(
				InputErrorCode.BackendUnavailable,
				"Нет графического сеанса: не заданы DISPLAY и WAYLAND_DISPLAY, а глобальному хуку нужен X11 или Wayland.",
				"Запустите приложение внутри графического сеанса.");
		}

		return BackendProbe.Available;
	}

	private static HookPlatform CurrentPlatform()
	{
		// На Android OperatingSystem.IsLinux() тоже true: исключаем его первым.
		if (OperatingSystem.IsAndroid())
		{
			return HookPlatform.Other;
		}

		if (OperatingSystem.IsWindows())
		{
			return HookPlatform.Windows;
		}

		if (OperatingSystem.IsMacOS())
		{
			return HookPlatform.MacOS;
		}

		return OperatingSystem.IsLinux() ? HookPlatform.Linux : HookPlatform.Other;
	}
}

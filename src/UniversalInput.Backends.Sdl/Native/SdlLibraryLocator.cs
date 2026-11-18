using System.Runtime.InteropServices;

namespace UniversalInput.Backends.Sdl.Native;

internal static class SdlLibraryLocator
{
	public static SdlPlatform CurrentPlatform =>
		OperatingSystem.IsWindows() ? SdlPlatform.Windows
		: OperatingSystem.IsMacOS() ? SdlPlatform.MacOS
		: OperatingSystem.IsLinux() ? SdlPlatform.Linux
		: SdlPlatform.Other;

	/// <summary>Имена для загрузки по порядку: заданный путь, затем стандартные имена ОС.</summary>
	public static IReadOnlyList<string> Candidates(SdlPlatform platform, string? customPath)
	{
		List<string> result = [];
		if (!string.IsNullOrWhiteSpace(customPath))
		{
			result.Add(customPath);
		}

		result.AddRange(platform switch
		{
			SdlPlatform.Windows => ["SDL3.dll", "SDL3"],
			SdlPlatform.MacOS => ["libSDL3.0.dylib", "libSDL3.dylib", "SDL3"],
			SdlPlatform.Linux => ["libSDL3.so.0", "libSDL3.so", "SDL3"],
			_ => ["SDL3"],
		});

		return result;
	}

	public static string Remediation(SdlPlatform platform) => platform switch
	{
		SdlPlatform.Windows => "Положите SDL3.dll рядом с приложением или укажите путь в SdlBackendOptions.LibraryPath.",
		SdlPlatform.MacOS => "Установите SDL3 (например, brew install sdl3) или положите libSDL3.dylib рядом с приложением.",
		SdlPlatform.Linux => "Установите SDL3 из пакетов дистрибутива (libsdl3-0 или SDL3) или положите libSDL3.so.0 рядом с приложением.",
		_ => "Укажите путь к библиотеке SDL3 в SdlBackendOptions.LibraryPath.",
	};

	public static InputException NotFound(SdlPlatform platform, IReadOnlyList<string> tried) =>
		new(InputErrorCode.BackendUnavailable, "Не найдена библиотека SDL3 (пробовали: " + string.Join(", ", tried) + ").")
		{
			Remediation = Remediation(platform),
		};

	/// <summary>Проверяет, что библиотека находится и загружается, но SDL не инициализирует.</summary>
	public static BackendProbe Probe(string? customPath)
	{
		if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsBrowser())
		{
			return BackendProbe.Unavailable(
				InputErrorCode.PlatformRestriction,
				"На этой платформе SDL3 не подключается как отдельная нативная библиотека; для Android и iOS предусмотрены отдельные backend.");
		}

		var platform = CurrentPlatform;
		var candidates = Candidates(platform, customPath);
		foreach (var candidate in candidates)
		{
			if (NativeLibrary.TryLoad(candidate, typeof(SdlLibraryLocator).Assembly, null, out var handle))
			{
				NativeLibrary.Free(handle);
				return BackendProbe.Available;
			}
		}

		var error = NotFound(platform, candidates);
		return BackendProbe.Unavailable(error.Code, error.Message, error.Remediation);
	}
}

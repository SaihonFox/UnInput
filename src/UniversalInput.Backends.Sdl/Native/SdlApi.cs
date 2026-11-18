using System.Runtime.InteropServices;

namespace UniversalInput.Backends.Sdl.Native;

/// <summary>
/// Нужные функции SDL3, загруженные по именам из нативной библиотеки. Подписи соответствуют документации SDL3:
/// <c>bool</c> SDL — один байт, SDL_JoystickID и SDL_PropertiesID — Uint32, SDL_Gamepad* — непрозрачный указатель.
/// Своя загрузка вместо готовой привязки: путь к библиотеке задаётся настройкой, а зависимостей не добавляется.
/// </summary>
internal sealed unsafe class SdlApi : IDisposable
{
	private nint _library;

	public delegate* unmanaged[Cdecl]<byte*, byte*, byte> SetHint;
	public delegate* unmanaged[Cdecl]<uint, byte> InitSubSystem;
	public delegate* unmanaged[Cdecl]<uint, void> QuitSubSystem;
	public delegate* unmanaged[Cdecl]<byte*> GetError;
	public delegate* unmanaged[Cdecl]<int*, uint*> GetGamepads;
	public delegate* unmanaged[Cdecl]<void*, void> Free;
	public delegate* unmanaged[Cdecl]<uint, nint> OpenGamepad;
	public delegate* unmanaged[Cdecl]<nint, void> CloseGamepad;
	public delegate* unmanaged[Cdecl]<nint, byte*> GetGamepadName;
	public delegate* unmanaged[Cdecl]<nint, ushort> GetGamepadVendor;
	public delegate* unmanaged[Cdecl]<nint, ushort> GetGamepadProduct;
	public delegate* unmanaged[Cdecl]<nint, byte*> GetGamepadSerial;
	public delegate* unmanaged[Cdecl]<nint, byte*> GetGamepadPath;
	public delegate* unmanaged[Cdecl]<nint, int, byte> GamepadHasSensor;
	public delegate* unmanaged[Cdecl]<nint, int, byte, byte> SetGamepadSensorEnabled;
	public delegate* unmanaged[Cdecl]<nint, ushort, ushort, uint, byte> RumbleGamepad;
	public delegate* unmanaged[Cdecl]<nint, uint> GetGamepadProperties;
	public delegate* unmanaged[Cdecl]<uint, byte*, byte, byte> GetBooleanProperty;
	public delegate* unmanaged[Cdecl]<byte*, int, byte> WaitEventTimeout;

	private SdlApi(nint library)
	{
		_library = library;
	}

	/// <summary>Загружает библиотеку из первого подходящего места и находит все функции; иначе бросает <see cref="InputException"/>.</summary>
	public static SdlApi Load(string? customPath)
	{
		var platform = SdlLibraryLocator.CurrentPlatform;
		var candidates = SdlLibraryLocator.Candidates(platform, customPath);

		nint library = 0;
		var found = false;
		foreach (var candidate in candidates)
		{
			if (NativeLibrary.TryLoad(candidate, typeof(SdlApi).Assembly, null, out library))
			{
				found = true;
				break;
			}
		}

		if (!found)
		{
			throw SdlLibraryLocator.NotFound(platform, candidates);
		}

		var api = new SdlApi(library);
		try
		{
			api.Resolve();
		}
		catch (EntryPointNotFoundException ex)
		{
			api.Dispose();
			throw new InputException(
				InputErrorCode.BackendUnavailable,
				"Найденная библиотека не похожа на SDL3 или слишком старая: " + ex.Message,
				ex)
			{
				Remediation = "Нужна SDL 3.2 или новее (SDL2 не подходит).",
			};
		}

		return api;
	}

	public void Dispose()
	{
		var library = Interlocked.Exchange(ref _library, 0);
		if (library != 0)
		{
			NativeLibrary.Free(library);
		}
	}

	private void Resolve()
	{
		SetHint = (delegate* unmanaged[Cdecl]<byte*, byte*, byte>)Export("SDL_SetHint");
		InitSubSystem = (delegate* unmanaged[Cdecl]<uint, byte>)Export("SDL_InitSubSystem");
		QuitSubSystem = (delegate* unmanaged[Cdecl]<uint, void>)Export("SDL_QuitSubSystem");
		GetError = (delegate* unmanaged[Cdecl]<byte*>)Export("SDL_GetError");
		GetGamepads = (delegate* unmanaged[Cdecl]<int*, uint*>)Export("SDL_GetGamepads");
		Free = (delegate* unmanaged[Cdecl]<void*, void>)Export("SDL_free");
		OpenGamepad = (delegate* unmanaged[Cdecl]<uint, nint>)Export("SDL_OpenGamepad");
		CloseGamepad = (delegate* unmanaged[Cdecl]<nint, void>)Export("SDL_CloseGamepad");
		GetGamepadName = (delegate* unmanaged[Cdecl]<nint, byte*>)Export("SDL_GetGamepadName");
		GetGamepadVendor = (delegate* unmanaged[Cdecl]<nint, ushort>)Export("SDL_GetGamepadVendor");
		GetGamepadProduct = (delegate* unmanaged[Cdecl]<nint, ushort>)Export("SDL_GetGamepadProduct");
		GetGamepadSerial = (delegate* unmanaged[Cdecl]<nint, byte*>)Export("SDL_GetGamepadSerial");
		GetGamepadPath = (delegate* unmanaged[Cdecl]<nint, byte*>)Export("SDL_GetGamepadPath");
		GamepadHasSensor = (delegate* unmanaged[Cdecl]<nint, int, byte>)Export("SDL_GamepadHasSensor");
		SetGamepadSensorEnabled = (delegate* unmanaged[Cdecl]<nint, int, byte, byte>)Export("SDL_SetGamepadSensorEnabled");
		RumbleGamepad = (delegate* unmanaged[Cdecl]<nint, ushort, ushort, uint, byte>)Export("SDL_RumbleGamepad");
		GetGamepadProperties = (delegate* unmanaged[Cdecl]<nint, uint>)Export("SDL_GetGamepadProperties");
		GetBooleanProperty = (delegate* unmanaged[Cdecl]<uint, byte*, byte, byte>)Export("SDL_GetBooleanProperty");
		WaitEventTimeout = (delegate* unmanaged[Cdecl]<byte*, int, byte>)Export("SDL_WaitEventTimeout");
	}

	private nint Export(string name) => NativeLibrary.GetExport(_library, name);
}

namespace UniversalInput.Backends.Sdl.Native;

internal static class SdlFailure
{
	public static InputException ToException(Exception error) => error switch
	{
		InputException input => input,
		DllNotFoundException or BadImageFormatException or EntryPointNotFoundException =>
			new InputException(InputErrorCode.BackendUnavailable, "Не удалось использовать библиотеку SDL3: " + error.Message, error)
			{
				Remediation = SdlLibraryLocator.Remediation(SdlLibraryLocator.CurrentPlatform),
			},
		_ => InputException.BackendFault("Сбой SDL: " + error.Message, error),
	};
}

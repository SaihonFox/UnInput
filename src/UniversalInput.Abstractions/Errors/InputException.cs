namespace UniversalInput.Abstractions.Errors;

/// <summary>
/// Ошибка операции ввода. Системные ограничения не маскируются: нативный код ошибки и
/// инструкция по исправлению сохраняются.
/// </summary>
public class InputException : Exception
{
	public InputException(InputErrorCode code, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		Code = code;
	}

	public InputErrorCode Code { get; }

	/// <summary>Что сделать, чтобы операция заработала (например, выдать разрешение).</summary>
	public string? Remediation { get; init; }

	/// <summary>Код ошибки платформы, если он есть.</summary>
	public int? NativeErrorCode { get; init; }

	public static InputException Unsupported(string message) => new(InputErrorCode.UnsupportedOperation, message);

	public static InputException DeviceUnavailable(string message, Exception? inner = null) =>
		new(InputErrorCode.DeviceUnavailable, message, inner);

	public static InputException DeviceBusy(string message) => new(InputErrorCode.DeviceBusy, message);

	public static InputException BackendUnavailable(string message, Exception? inner = null) =>
		new(InputErrorCode.BackendUnavailable, message, inner);

	public static InputException BackendFault(string message, Exception? inner = null) =>
		new(InputErrorCode.BackendFault, message, inner);
}

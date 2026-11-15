namespace UniversalInput.Abstractions.Backends;

/// <summary>Результат проверки backend: ОС, права, нативные библиотеки.</summary>
public sealed record BackendProbe
{
	public bool IsAvailable { get; init; }

	/// <summary>Код причины, если backend недоступен.</summary>
	public InputErrorCode? FailureCode { get; init; }

	public string? Reason { get; init; }
	public string? Remediation { get; init; }

	public static BackendProbe Available { get; } = new() { IsAvailable = true };

	public static BackendProbe Unavailable(InputErrorCode code, string reason, string? remediation = null) =>
		new() { IsAvailable = false, FailureCode = code, Reason = reason, Remediation = remediation };
}

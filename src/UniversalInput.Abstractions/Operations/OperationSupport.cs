namespace UniversalInput.Abstractions.Operations;

/// <summary>Ответ на вопрос «можно ли выполнить операцию»: состояние, причина и что делать.</summary>
public readonly record struct OperationSupport(
	SupportState State,
	string? Reason = null,
	string? Remediation = null)
{
	public bool IsSupported => State == SupportState.Supported;

	public static OperationSupport Supported { get; } = new(SupportState.Supported);

	public static OperationSupport Unsupported(string reason) => new(SupportState.Unsupported, reason);
}

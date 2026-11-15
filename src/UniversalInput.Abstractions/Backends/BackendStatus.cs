namespace UniversalInput.Abstractions.Backends;

/// <summary>Состояние backend, как его видит приложение: причина недоступности и выбранные роли.</summary>
public sealed record BackendStatus
{
	public required string Id { get; init; }
	public required ushort Index { get; init; }
	public required BackendState State { get; init; }

	/// <summary>Код последней ошибки или причины недоступности.</summary>
	public InputErrorCode? ErrorCode { get; init; }

	public string? Reason { get; init; }
	public string? Remediation { get; init; }

	/// <summary>Сколько раз Core перезапускал backend после восстановимых ошибок.</summary>
	public int RestartCount { get; init; }

	/// <summary>Роли (операции), за которые backend отвечает.</summary>
	public IReadOnlyCollection<InputOperation> Roles { get; init; } = [];
}

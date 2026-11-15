namespace UniversalInput.Abstractions.Operations;

/// <summary>Честное состояние поддержки операции.</summary>
public enum SupportState
{
	Supported,
	RequiresPermission,
	RestrictedByPlatform,
	Unsupported,
}

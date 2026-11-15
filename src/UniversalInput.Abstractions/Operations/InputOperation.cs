namespace UniversalInput.Abstractions.Operations;

/// <summary>Что можно сделать с устройством через конкретный backend.</summary>
public enum InputOperation
{
	Enumerate,
	Observe,
	Intercept,
	Emulate,
	DirectAccess,
	Output,
}

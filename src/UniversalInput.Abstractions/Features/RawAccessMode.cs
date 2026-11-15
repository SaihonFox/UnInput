namespace UniversalInput.Abstractions.Features;

[Flags]
public enum RawAccessMode
{
	Read = 1,
	Write = 2,
	ReadWrite = Read | Write,
}

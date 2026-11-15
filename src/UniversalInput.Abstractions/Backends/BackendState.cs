namespace UniversalInput.Abstractions.Backends;

public enum BackendState
{
	Created,
	Probing,
	Unavailable,
	Ready,
	Starting,
	Running,
	Faulted,
	Stopping,
	Stopped,
	Disposed,
}

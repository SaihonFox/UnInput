using UniversalInput.Backends.GlobalHook.Sources;

namespace UniversalInput.Backends.GlobalHook.Tests.Fakes;

/// <summary>Хук без ОС: тест сам «нажимает клавиши» через <see cref="Raise"/>.</summary>
internal sealed class FakeHookSource : IHookSource
{
	public event Action<HookSignal>? Signal;

	public event Action<Exception>? Failed;

	public Func<HookSignal, bool>? ShouldSuppress { get; set; }

	/// <summary>Если задано, <see cref="StartAsync"/> бросает это исключение.</summary>
	public Exception? StartFailure { get; set; }

	public bool Started { get; private set; }

	public bool Disposed { get; private set; }

	public ValueTask StartAsync(CancellationToken cancellationToken)
	{
		if (StartFailure is not null)
		{
			throw StartFailure;
		}

		Started = true;
		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		Disposed = true;
		return ValueTask.CompletedTask;
	}

	public void Raise(HookSignal signal) => Signal?.Invoke(signal);

	/// <summary>Как поток хука: спрашивает, подавить ли событие, и только потом сообщает его.</summary>
	public bool RaiseAndGetSuppressed(HookSignal signal)
	{
		var suppressed = ShouldSuppress?.Invoke(signal) == true;
		Signal?.Invoke(signal);
		return suppressed;
	}

	public void Fail(Exception error) => Failed?.Invoke(error);
}

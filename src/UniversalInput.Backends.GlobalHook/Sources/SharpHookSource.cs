using SharpHook;
using SharpHook.Data;
using HookMouseButton = SharpHook.Data.MouseButton;
using InputMouseButton = UniversalInput.Abstractions.Events.MouseButton;
using UniversalInput.Backends.GlobalHook.Mapping;

namespace UniversalInput.Backends.GlobalHook.Sources;

/// <summary>
/// Хук на SharpHook (libuiohook). События обрабатываются прямо в потоке хука (<see cref="SimpleGlobalHook"/>):
/// запись события в очередь backend не блокируется, а лишний поток и очередь добавили бы задержку.
/// Относительное движение (<c>MouseMovedRelative</c>) бывает только на Wayland и пока не используется.
/// </summary>
internal sealed class SharpHookSource : IHookSource
{
	private SimpleGlobalHook? _hook;
	private Task _run = Task.CompletedTask;
	private volatile bool _started;
	private int _disposed;

	public event Action<HookSignal>? Signal;

	public event Action<Exception>? Failed;

	public async ValueTask StartAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed != 0, this);

		var hook = new SimpleGlobalHook();
		_hook = hook;

		hook.KeyPressed += (_, e) => EmitKey(HookSignalKind.KeyDown, e);
		hook.KeyReleased += (_, e) => EmitKey(HookSignalKind.KeyUp, e);
		hook.MouseMoved += (_, e) => EmitMouse(HookSignalKind.MouseMove, e, withButton: false);
		hook.MouseDragged += (_, e) => EmitMouse(HookSignalKind.MouseMove, e, withButton: false);
		hook.MousePressed += (_, e) => EmitMouse(HookSignalKind.MouseDown, e, withButton: true);
		hook.MouseReleased += (_, e) => EmitMouse(HookSignalKind.MouseUp, e, withButton: true);
		hook.MouseWheel += (_, e) => EmitWheel(e);

		var enabled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		hook.HookEnabled += (_, _) => enabled.TrySetResult();

		var run = hook.RunAsync();
		_run = run;

		var finished = await Task.WhenAny(enabled.Task, run).WaitAsync(cancellationToken).ConfigureAwait(false);
		if (!ReferenceEquals(finished, enabled.Task))
		{
			// Хук завершился, так и не включившись: причина — в исключении задачи.
			await run.ConfigureAwait(false);
			throw new InvalidOperationException("Глобальный хук остановился сразу после запуска.");
		}

		_started = true;
		_ = run.ContinueWith(
			OnRunEnded, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_started = false;
		var hook = _hook;
		if (hook is null)
		{
			return;
		}

		try
		{
			hook.Stop();
		}
		catch (Exception)
		{
			// Хук мог уже остановиться или не запускаться: при освобождении это не ошибка.
		}

		try
		{
			await _run.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
		}
		catch (Exception)
		{
			// Ошибка запуска уже передана вызывающему; здесь важно только не зависнуть.
		}

		hook.Dispose();
	}

	private void OnRunEnded(Task run)
	{
		if (!_started)
		{
			return;
		}

		_started = false;
		Failed?.Invoke(run.Exception?.GetBaseException() ?? new InvalidOperationException("Глобальный хук остановился."));
	}

	private void EmitKey(HookSignalKind kind, KeyboardHookEventArgs e)
	{
		if (!KeyMap.TryGetUsage(e.Data.KeyCode, out var usage))
		{
			return;
		}

		Signal?.Invoke(new HookSignal
		{
			Kind = kind,
			Usage = usage,
			Modifiers = ModifierMap.From(e.RawEvent.Mask),
			Simulated = e.IsEventSimulated,
		});
	}

	private void EmitMouse(HookSignalKind kind, MouseHookEventArgs e, bool withButton)
	{
		var data = e.Data;
		InputMouseButton button = default;
		if (withButton && !TryMapButton(data.Button, out button))
		{
			return;
		}

		Signal?.Invoke(new HookSignal
		{
			Kind = kind,
			Button = button,
			X = data.X,
			Y = data.Y,
			Modifiers = ModifierMap.From(e.RawEvent.Mask),
			Simulated = e.IsEventSimulated,
		});
	}

	/// <summary>SharpHook: положительное вращение — вверх или влево; у нас Y &gt; 0 — вверх, X &gt; 0 — вправо. Единицы — как у libuiohook (по ОС различаются).</summary>
	private void EmitWheel(MouseWheelHookEventArgs e)
	{
		float rotation = e.Data.Rotation;
		var horizontal = e.Data.Direction == MouseWheelScrollDirection.Horizontal;

		Signal?.Invoke(new HookSignal
		{
			Kind = HookSignalKind.Wheel,
			X = horizontal ? -rotation : 0f,
			Y = horizontal ? 0f : rotation,
			Modifiers = ModifierMap.From(e.RawEvent.Mask),
			Simulated = e.IsEventSimulated,
		});
	}

	private static bool TryMapButton(HookMouseButton source, out InputMouseButton button)
	{
		switch (source)
		{
			case HookMouseButton.Button1:
				button = InputMouseButton.Left;
				return true;
			case HookMouseButton.Button2:
				button = InputMouseButton.Right;
				return true;
			case HookMouseButton.Button3:
				button = InputMouseButton.Middle;
				return true;
			case HookMouseButton.Button4:
				button = InputMouseButton.X1;
				return true;
			case HookMouseButton.Button5:
				button = InputMouseButton.X2;
				return true;
			default:
				button = default;
				return false;
		}
	}
}

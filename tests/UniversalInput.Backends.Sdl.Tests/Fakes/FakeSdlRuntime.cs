using System.Collections.Concurrent;
using UniversalInput.Backends.Sdl.Runtime;

namespace UniversalInput.Backends.Sdl.Tests.Fakes;

internal sealed class FakeSdlRuntime : ISdlRuntime
{
	private readonly BlockingCollection<SdlEvent> _events = new();
	private readonly Dictionary<uint, SdlGamepadInfo> _gamepads = [];
	private readonly object _gate = new();
	private readonly List<uint> _closed = [];
	private readonly List<(uint Instance, ushort Low, ushort High, uint Milliseconds)> _rumbles = [];
	private Exception? _waitFailure;

	/// <summary>Если задано, <see cref="Initialize"/> бросает это исключение.</summary>
	public Exception? InitializeFailure { get; set; }

	/// <summary>Если задано, <see cref="Rumble"/> бросает это исключение.</summary>
	public Exception? RumbleFailure { get; set; }

	/// <summary>Геймпады, которые <see cref="OpenGamepad"/> «не смог открыть».</summary>
	public HashSet<uint> OpenFailures { get; } = [];

	public bool? AllowBackgroundEvents { get; private set; }

	public bool? EnableSensorsRequested { get; private set; }

	public bool Disposed { get; private set; }

	/// <summary>Потоки, из которых вызывались методы, обязанные работать в потоке цикла.</summary>
	public ConcurrentBag<int> LoopThreads { get; } = new();

	public IReadOnlyList<uint> Closed
	{
		get
		{
			lock (_gate)
			{
				return [.. _closed];
			}
		}
	}

	public IReadOnlyList<(uint Instance, ushort Low, ushort High, uint Milliseconds)> Rumbles
	{
		get
		{
			lock (_gate)
			{
				return [.. _rumbles];
			}
		}
	}

	public static SdlGamepadInfo Pad(
		uint instance,
		string name = "Pad",
		int vendor = 0x045E,
		int product = 0x028E,
		string? serial = "S1",
		string? path = null,
		bool rumble = true,
		bool accelerometer = false,
		bool gyroscope = false) => new(instance, name, vendor, product, serial, path ?? "/dev/input/event" + instance, rumble, accelerometer, gyroscope);

	/// <summary>Подключённый геймпад: виден при первичном перечислении и открывается по запросу.</summary>
	public void Plug(SdlGamepadInfo info)
	{
		lock (_gate)
		{
			_gamepads[info.InstanceId] = info;
		}
	}

	public void Push(SdlEvent sdlEvent) => _events.Add(sdlEvent);

	/// <summary>Следующий вызов <see cref="TryWaitEvent"/> бросит это исключение.</summary>
	public void FailNextWait(Exception error) => Interlocked.Exchange(ref _waitFailure, error);

	public void Initialize(bool allowBackgroundEvents)
	{
		LoopThreads.Add(Environment.CurrentManagedThreadId);
		AllowBackgroundEvents = allowBackgroundEvents;
		if (InitializeFailure is { } failure)
		{
			throw failure;
		}
	}

	public IReadOnlyList<uint> GetGamepadIds()
	{
		LoopThreads.Add(Environment.CurrentManagedThreadId);
		lock (_gate)
		{
			return [.. _gamepads.Keys];
		}
	}

	public SdlGamepadInfo? OpenGamepad(uint instanceId, bool enableSensors)
	{
		LoopThreads.Add(Environment.CurrentManagedThreadId);
		EnableSensorsRequested = enableSensors;
		lock (_gate)
		{
			return !OpenFailures.Contains(instanceId) && _gamepads.TryGetValue(instanceId, out var info) ? info : null;
		}
	}

	public void CloseGamepad(uint instanceId)
	{
		LoopThreads.Add(Environment.CurrentManagedThreadId);
		lock (_gate)
		{
			_closed.Add(instanceId);
		}
	}

	public bool TryWaitEvent(TimeSpan timeout, out SdlEvent sdlEvent)
	{
		LoopThreads.Add(Environment.CurrentManagedThreadId);
		if (Interlocked.Exchange(ref _waitFailure, null) is { } failure)
		{
			throw failure;
		}

		return _events.TryTake(out sdlEvent, (int)timeout.TotalMilliseconds);
	}

	public void Rumble(uint instanceId, ushort lowFrequency, ushort highFrequency, uint durationMilliseconds)
	{
		if (RumbleFailure is { } failure)
		{
			throw failure;
		}

		lock (_gate)
		{
			_rumbles.Add((instanceId, lowFrequency, highFrequency, durationMilliseconds));
		}
	}

	public void Dispose() => Disposed = true;
}

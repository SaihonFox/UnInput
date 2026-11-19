using System.Collections.Concurrent;
using UniversalInput.Backends.Hid.Descriptors;
using UniversalInput.Backends.Hid.Discovery;
using UniversalInput.Backends.Hid.Platform;

namespace UniversalInput.Backends.Hid.Tests.Fakes;

internal sealed class FakeHidPlatform : IHidPlatform
{
	private readonly object _gate = new();
	private readonly List<Action> _handlers = [];
	private IReadOnlyList<HidDeviceInfo> _devices = [];
	private int _failuresRemaining;

	public ConcurrentDictionary<string, FakeHidConnection> Connections { get; } = new();

	/// <summary>Если задано, <see cref="GetDevices"/> бросает это исключение (столько раз, сколько в <see cref="FailuresRemaining"/>).</summary>
	public Exception? GetDevicesFailure { get; set; }

	public int FailuresRemaining
	{
		get => Volatile.Read(ref _failuresRemaining);
		set => Volatile.Write(ref _failuresRemaining, value);
	}

	/// <summary>Если задано, <see cref="Open"/> бросает это исключение.</summary>
	public Exception? OpenFailure { get; set; }

	public int SubscriberCount
	{
		get
		{
			lock (_gate)
			{
				return _handlers.Count;
			}
		}
	}

	public static HidDeviceInfo Keyboard(string path, string? product = "Keyboard", string? serial = "SN1") => new(
		path, 0x046D, 0xC31C, 0x0100, "Logitech", product, serial, 8, [new HidUsage(0x01, 0x06)]);

	public static HidDeviceInfo KeyboardWithDescriptor(string path) => new(
		path, 0x046D, 0xC31C, 0x0100, "Logitech", "Keyboard", "SN1", 9, [new HidUsage(0x01, 0x06)], HidTestDescriptors.BootKeyboard);

	public static HidDeviceInfo MouseWithDescriptor(string path) => new(
		path, 0x046D, 0xC077, 0x0100, "Logitech", "Mouse", "SN2", 5, [new HidUsage(0x01, 0x02)], HidTestDescriptors.WheelMouse);

	public static HidDeviceInfo GamepadWithDescriptor(string path) => new(
		path, 0x054C, 0x09CC, 0x0100, "Sony", "Pad", null, 2, [new HidUsage(0x01, 0x05)], HidTestDescriptors.Gamepad);

	public static HidDeviceInfo Vendor(string path) => new(
		path, 0x1234, 0x0001, 0x0001, null, null, null, 64, [new HidUsage(0xFF00, 0x01)]);

	public void SetDevices(params HidDeviceInfo[] devices)
	{
		lock (_gate)
		{
			_devices = devices;
		}
	}

	/// <summary>Сообщает backend о смене списка, как это делает HidSharp.</summary>
	public void RaiseChanged()
	{
		Action[] handlers;
		lock (_gate)
		{
			handlers = _handlers.ToArray();
		}

		foreach (var handler in handlers)
		{
			handler();
		}
	}

	public IReadOnlyList<HidDeviceInfo> GetDevices()
	{
		if (GetDevicesFailure is { } failure)
		{
			if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
			{
				throw failure;
			}
		}

		lock (_gate)
		{
			return _devices;
		}
	}

	public IDisposable SubscribeChanged(Action handler)
	{
		lock (_gate)
		{
			_handlers.Add(handler);
		}

		return new Unsubscriber(() =>
		{
			lock (_gate)
			{
				_handlers.Remove(handler);
			}
		});
	}

	public IHidConnection Open(string path)
	{
		if (OpenFailure is { } failure)
		{
			throw failure;
		}

		return Connections.GetOrAdd(path, _ => new FakeHidConnection());
	}

	private sealed class Unsubscriber(Action action) : IDisposable
	{
		public void Dispose() => action();
	}
}

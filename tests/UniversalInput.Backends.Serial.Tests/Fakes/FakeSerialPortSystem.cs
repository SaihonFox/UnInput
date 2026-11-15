using System.Collections.Concurrent;
using UniversalInput.Backends.Serial.Options;
using UniversalInput.Backends.Serial.Ports;

namespace UniversalInput.Backends.Serial.Tests.Fakes;

/// <summary>Подменяет ОС: список портов и открытые соединения задаёт тест.</summary>
internal sealed class FakeSerialPortSystem : ISerialPortSystem
{
	private readonly object _gate = new();
	private List<string> _ports = [];
	private Exception? _listFailure;
	private int _listFailuresRemaining;
	private int _listCalls;

	public ConcurrentDictionary<string, FakeSerialConnection> Connections { get; } = new();

	/// <summary>Если задано, <see cref="OpenAsync"/> бросает это исключение.</summary>
	public Exception? OpenFailure { get; set; }

	public int ListCalls => Volatile.Read(ref _listCalls);

	public void SetPorts(params string[] names)
	{
		lock (_gate)
		{
			_ports = [.. names];
		}
	}

	/// <summary>Ближайшие <paramref name="times"/> вызовов перечисления завершатся этой ошибкой.</summary>
	public void FailListing(Exception error, int times = 1)
	{
		lock (_gate)
		{
			_listFailure = error;
			_listFailuresRemaining = times;
		}
	}

	public IReadOnlyList<string> GetPortNames()
	{
		Interlocked.Increment(ref _listCalls);
		lock (_gate)
		{
			if (_listFailuresRemaining > 0 && _listFailure is not null)
			{
				_listFailuresRemaining--;
				throw _listFailure;
			}

			return [.. _ports];
		}
	}

	public ValueTask<ISerialConnection> OpenAsync(
		string portName, SerialBackendOptions options, CancellationToken cancellationToken)
	{
		if (OpenFailure is not null)
		{
			throw OpenFailure;
		}

		var connection = new FakeSerialConnection();
		Connections[portName] = connection;
		return new ValueTask<ISerialConnection>(connection);
	}
}

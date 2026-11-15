using UniversalInput.Backends.Serial.Ports;

namespace UniversalInput.Backends.Serial.Channels;

/// <summary>
/// Канал порта для прямого доступа. «Report» здесь — порция байтов: последовательный порт не делит поток на пакеты.
/// Потерю порта (отключение адаптера) превращает в <see cref="InputErrorCode.DeviceUnavailable"/>.
/// </summary>
internal sealed class SerialChannel : IRawDeviceChannel
{
	private readonly ISerialConnection _connection;
	private readonly RawAccessMode _mode;
	private readonly string _portName;

	public SerialChannel(ISerialConnection connection, RawAccessMode mode, string portName)
	{
		_connection = connection;
		_mode = mode;
		_portName = portName;
	}

	public async ValueTask<int> ReadAsync(Memory<byte> report, CancellationToken cancellationToken)
	{
		if ((_mode & RawAccessMode.Read) == 0)
		{
			throw InputException.Unsupported($"Порт {_portName} открыт без права чтения.");
		}

		try
		{
			return await _connection.ReadAsync(report, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (IsPortLost(ex))
		{
			throw InputException.DeviceUnavailable($"Порт {_portName} отключён.", ex);
		}
	}

	public async ValueTask WriteAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
	{
		if ((_mode & RawAccessMode.Write) == 0)
		{
			throw InputException.Unsupported($"Порт {_portName} открыт без права записи.");
		}

		try
		{
			await _connection.WriteAsync(report, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (IsPortLost(ex))
		{
			throw InputException.DeviceUnavailable($"Порт {_portName} отключён.", ex);
		}
	}

	public ValueTask DisposeAsync() => _connection.DisposeAsync();

	private static bool IsPortLost(Exception exception) =>
		exception is IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException;
}

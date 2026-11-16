using UniversalInput.Backends.Hid.Platform;

namespace UniversalInput.Backends.Hid.Channels;

/// <summary>
/// Канал HID-устройства для прямого доступа. Отчёты передаются как есть: первый байт — идентификатор отчёта
/// (0, если нумерации нет). Потерю устройства превращает в <see cref="InputErrorCode.DeviceUnavailable"/>.
/// </summary>
internal sealed class HidChannel : IRawDeviceChannel
{
	private readonly IHidConnection _connection;
	private readonly RawAccessMode _mode;
	private readonly string _path;
	private readonly int _maxInputReportLength;

	public HidChannel(IHidConnection connection, RawAccessMode mode, string path, int maxInputReportLength)
	{
		_connection = connection;
		_mode = mode;
		_path = path;
		_maxInputReportLength = maxInputReportLength;
	}

	public async ValueTask<int> ReadAsync(Memory<byte> report, CancellationToken cancellationToken)
	{
		if ((_mode & RawAccessMode.Read) == 0)
		{
			throw InputException.Unsupported($"HID-устройство {_path} открыто без права чтения.");
		}

		// HidSharp требует буфер не меньше самого длинного входного отчёта; тихо обрезать отчёт мы не станем.
		if (_maxInputReportLength > 0 && report.Length < _maxInputReportLength)
		{
			throw new ArgumentException(
				$"Буфер ({report.Length} Б) меньше самого длинного входного отчёта устройства ({_maxInputReportLength} Б).",
				nameof(report));
		}

		try
		{
			return await _connection.ReadAsync(report, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (IsDeviceLost(ex))
		{
			throw InputException.DeviceUnavailable($"HID-устройство {_path} отключено.", ex);
		}
	}

	public async ValueTask WriteAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
	{
		if ((_mode & RawAccessMode.Write) == 0)
		{
			throw InputException.Unsupported($"HID-устройство {_path} открыто без права записи.");
		}

		try
		{
			await _connection.WriteAsync(report, cancellationToken).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			throw InputException.DeviceBusy($"HID-устройство {_path} не приняло отчёт вовремя.");
		}
		catch (Exception ex) when (IsDeviceLost(ex))
		{
			throw InputException.DeviceUnavailable($"HID-устройство {_path} отключено.", ex);
		}
	}

	public ValueTask DisposeAsync() => _connection.DisposeAsync();

	private static bool IsDeviceLost(Exception exception) =>
		exception is IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException;
}

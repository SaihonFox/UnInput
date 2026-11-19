using UniversalInput.Backends.Hid.Decoding;
using UniversalInput.Backends.Hid.Platform;

namespace UniversalInput.Backends.Hid.Observation;

/// <summary>
/// Читает входные отчёты открытого HID-устройства и превращает их в события ввода. Работает, пока устройство не отключится
/// или наблюдение не остановят. Потеря устройства — обычное завершение: сам список устройств обновляет backend.
/// </summary>
internal sealed class HidObserver
{
	private readonly IHidConnection _connection;
	private readonly HidInputDecoder _decoder;
	private readonly DeviceId _device;
	private readonly IBackendContext _context;
	private readonly int _bufferLength;
	private readonly CancellationTokenSource _cts = new();
	private Task _loop = Task.CompletedTask;
	private int _stopped;

	public HidObserver(
		IHidConnection connection, HidInputDecoder decoder, DeviceId device, IBackendContext context, int bufferLength)
	{
		_connection = connection;
		_decoder = decoder;
		_device = device;
		_context = context;
		_bufferLength = bufferLength;
	}

	public void Start() => _loop = Task.Run(RunAsync, CancellationToken.None);

	/// <summary>Идемпотентна. Закрытие соединения прерывает блокирующее чтение, которое не понимает отмену.</summary>
	public async Task StopAsync()
	{
		if (Interlocked.Exchange(ref _stopped, 1) != 0)
		{
			return;
		}

		await _cts.CancelAsync().ConfigureAwait(false);
		try
		{
			await _connection.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception ex) when (IsDeviceLost(ex))
		{
			// Устройство уже пропало: закрывать нечего.
		}

		try
		{
			await _loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			// Чтение зависло в драйвере: ждать его дольше нет смысла.
		}

		_cts.Dispose();
	}

	private async Task RunAsync()
	{
		var buffer = new byte[_bufferLength];
		var token = _cts.Token;
		try
		{
			while (!token.IsCancellationRequested)
			{
				var length = await _connection.ReadAsync(buffer, token).ConfigureAwait(false);
				if (length <= 0)
				{
					continue;
				}

				_decoder.Decode(_device, buffer.AsSpan(0, length), e => _context.TryWrite(in e));
			}
		}
		catch (Exception ex) when (ex is OperationCanceledException || IsDeviceLost(ex) || token.IsCancellationRequested)
		{
			// Остановка или отключение устройства.
		}
		catch (Exception ex)
		{
			_context.ReportFault(InputException.BackendFault($"Ошибка чтения HID-отчётов устройства {_device}: {ex.Message}", ex), recoverable: true);
		}
	}

	private static bool IsDeviceLost(Exception exception) =>
		exception is IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException or InputException;
}

namespace UniversalInput.Core.Raw;

/// <summary>
/// Обёртка над каналом backend: освобождает «слот» устройства (повторное открытие — DeviceBusy) и
/// закрывается Core при отключении устройства, а ошибки чтения после отключения превращает в DeviceUnavailable.
/// </summary>
internal sealed class RawChannelHandle : IRawDeviceChannel
{
	private readonly IRawDeviceChannel _inner;
	private readonly Action _released;
	private volatile bool _closed;
	private int _disposed;

	public RawChannelHandle(IRawDeviceChannel inner, Action released)
	{
		_inner = inner;
		_released = released;
	}

	public async ValueTask<int> ReadAsync(Memory<byte> report, CancellationToken cancellationToken)
	{
		ThrowIfClosed();
		try
		{
			return await _inner.ReadAsync(report, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (_closed && ex is not InputException && ex is not OperationCanceledException)
		{
			throw InputException.DeviceUnavailable("Устройство отключено во время чтения.", ex);
		}
	}

	public async ValueTask WriteAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
	{
		ThrowIfClosed();
		try
		{
			await _inner.WriteAsync(report, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (_closed && ex is not InputException && ex is not OperationCanceledException)
		{
			throw InputException.DeviceUnavailable("Устройство отключено во время записи.", ex);
		}
	}

	/// <summary>Закрывает канал из-за отключения устройства; не блокирует вызывающего.</summary>
	public void Close()
	{
		_closed = true;
		_ = DisposeInnerAsync();
	}

	public ValueTask DisposeAsync() => new(DisposeInnerAsync());

	private async Task DisposeInnerAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		try
		{
			await _inner.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception)
		{
			// Канал уже недоступен; ошибка освобождения не важна.
		}
		finally
		{
			_released();
		}
	}

	private void ThrowIfClosed()
	{
		if (_closed)
		{
			throw InputException.DeviceUnavailable("Канал закрыт: устройство отключено.");
		}
	}
}

namespace UniversalInput.Backends.Serial.Options;

/// <summary>Параметры backend. Применяются ко всем портам: по порту настройки не различаются.</summary>
public sealed class SerialBackendOptions
{
	/// <summary>
	/// Как часто опрашивать список портов. В System.IO.Ports события подключения не заявлены,
	/// поэтому обнаружение — опрос <c>SerialPort.GetPortNames()</c> раз в 1–2 секунды.
	/// </summary>
	public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1.5);

	public int BaudRate { get; set; } = 115200;

	public int DataBits { get; set; } = 8;

	public SerialParity Parity { get; set; } = SerialParity.None;

	public SerialStopBits StopBits { get; set; } = SerialStopBits.One;

	public void Validate()
	{
		if (PollInterval <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(PollInterval), PollInterval, "Интервал опроса должен быть положительным.");
		}

		if (BaudRate <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(BaudRate), BaudRate, "Скорость должна быть положительной.");
		}

		if (DataBits is < 5 or > 8)
		{
			throw new ArgumentOutOfRangeException(nameof(DataBits), DataBits, "Число бит данных — от 5 до 8.");
		}
	}
}

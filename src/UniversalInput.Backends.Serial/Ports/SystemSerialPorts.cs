using System.IO.Ports;
using UniversalInput.Backends.Serial.Options;

namespace UniversalInput.Backends.Serial.Ports;

internal sealed class SystemSerialPorts : ISerialPortSystem
{
	public IReadOnlyList<string> GetPortNames() => SerialPort.GetPortNames();

	public async ValueTask<ISerialConnection> OpenAsync(
		string portName, SerialBackendOptions options, CancellationToken cancellationToken)
	{
		return await Task.Run(
			() =>
			{
				var port = new SerialPort(
					portName, options.BaudRate, MapParity(options.Parity), options.DataBits, MapStopBits(options.StopBits));
				try
				{
					port.Open();
				}
				catch
				{
					port.Dispose();
					throw;
				}

				return (ISerialConnection)new SystemSerialConnection(port);
			},
			cancellationToken).ConfigureAwait(false);
	}

	private static Parity MapParity(SerialParity parity) => parity switch
	{
		SerialParity.None => Parity.None,
		SerialParity.Odd => Parity.Odd,
		SerialParity.Even => Parity.Even,
		SerialParity.Mark => Parity.Mark,
		SerialParity.Space => Parity.Space,
		_ => throw new ArgumentOutOfRangeException(nameof(parity), parity, null),
	};

	private static StopBits MapStopBits(SerialStopBits stopBits) => stopBits switch
	{
		SerialStopBits.One => StopBits.One,
		SerialStopBits.OnePointFive => StopBits.OnePointFive,
		SerialStopBits.Two => StopBits.Two,
		_ => throw new ArgumentOutOfRangeException(nameof(stopBits), stopBits, null),
	};
}

using UniversalInput.Backends.Serial.Options;

namespace UniversalInput.Backends.Serial.Ports;

/// <summary>Работа ОС с последовательными портами: перечисление и открытие.</summary>
internal interface ISerialPortSystem
{
	IReadOnlyList<string> GetPortNames();

	ValueTask<ISerialConnection> OpenAsync(string portName, SerialBackendOptions options, CancellationToken cancellationToken);
}

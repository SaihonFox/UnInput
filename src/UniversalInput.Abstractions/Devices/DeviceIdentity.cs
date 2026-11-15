namespace UniversalInput.Abstractions.Devices;

/// <summary>Сведения, по которым Core сопоставляет одно физическое устройство между backend.</summary>
public sealed record DeviceIdentity(
	ushort? VendorId = null,
	ushort? ProductId = null,
	string? SerialNumber = null,
	string? Path = null);

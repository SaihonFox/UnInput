namespace UniversalInput.Backends.Sdl.Runtime;

/// <summary>Сведения об открытом геймпаде. <see cref="Path"/> и <see cref="Serial"/> SDL сообщает не для всех устройств.</summary>
internal sealed record SdlGamepadInfo(
	uint InstanceId,
	string Name,
	int VendorId,
	int ProductId,
	string? Serial,
	string? Path,
	bool HasRumble,
	bool HasAccelerometer,
	bool HasGyroscope);

namespace UniversalInput.Backends.Sdl.Runtime;

/// <summary>Сведения об открытом джойстике без раскладки геймпада: число осей, кнопок и «шляп» (hat) сообщает устройство.</summary>
internal sealed record SdlJoystickInfo(
	uint InstanceId,
	string Name,
	int VendorId,
	int ProductId,
	string? Serial,
	string? Path,
	int Axes,
	int Buttons,
	int Hats);

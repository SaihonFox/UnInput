namespace UniversalInput.Core.Devices;

/// <summary>Один backend, поставляющий устройство. У логического устройства может быть несколько поставщиков.</summary>
internal sealed record DeviceProvider(ushort Backend, string BackendId, DeviceDescriptor Descriptor);

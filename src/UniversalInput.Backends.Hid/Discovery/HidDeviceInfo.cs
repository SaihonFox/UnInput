using UniversalInput.Backends.Hid.Descriptors;

namespace UniversalInput.Backends.Hid.Discovery;

/// <summary>Сведения об одном HID-интерфейсе, которые ОС сообщает без открытия устройства.</summary>
/// <param name="Path">Путь устройства в ОС. Один физический прибор может давать несколько интерфейсов с разными путями.</param>
/// <param name="ReportDescriptor">Сырой дескриптор отчётов; <c>null</c>, если получить его не удалось.</param>
/// <param name="Usages">Коллекции верхнего уровня из дескриптора отчётов; пусто, если дескриптор получить не удалось.</param>
public sealed record HidDeviceInfo(
	string Path,
	int VendorId,
	int ProductId,
	int ReleaseNumber,
	string? Manufacturer,
	string? Product,
	string? SerialNumber,
	int MaxInputReportLength,
	IReadOnlyList<HidUsage> Usages,
	byte[]? ReportDescriptor = null);

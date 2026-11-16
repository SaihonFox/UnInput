using HidSharp;
using UniversalInput.Backends.Hid.Descriptors;
using UniversalInput.Backends.Hid.Discovery;

namespace UniversalInput.Backends.Hid.Platform;

internal sealed class HidSharpPlatform : IHidPlatform
{
	public IReadOnlyList<HidDeviceInfo> GetDevices()
	{
		List<HidDeviceInfo> result = [];
		foreach (var device in DeviceList.Local.GetHidDevices())
		{
			// Устройство могло исчезнуть между перечислением и чтением его свойств: пропускаем его, следующий пересчёт всё выровняет.
			if (TryDescribe(device) is { } info)
			{
				result.Add(info);
			}
		}

		return result;
	}

	public IDisposable SubscribeChanged(Action handler)
	{
		ArgumentNullException.ThrowIfNull(handler);

		void OnChanged(object? sender, DeviceListChangedEventArgs e) => handler();
		DeviceList.Local.Changed += OnChanged;
		return new Subscription(() => DeviceList.Local.Changed -= OnChanged);
	}

	public IHidConnection Open(string path)
	{
		var device = DeviceList.Local.GetHidDevices().FirstOrDefault(d => string.Equals(d.DevicePath, path, StringComparison.Ordinal))
			?? throw InputException.DeviceUnavailable($"HID-устройство {path} не найдено или отключено.");
		return new HidSharpConnection(device.Open());
	}

	private static HidDeviceInfo? TryDescribe(HidDevice device)
	{
		try
		{
			return new HidDeviceInfo(
				device.DevicePath,
				device.VendorID,
				device.ProductID,
				device.ReleaseNumberBcd,
				Try(device.GetManufacturer),
				Try(device.GetProductName),
				Try(device.GetSerialNumber),
				Try(device.GetMaxInputReportLength, 0),
				Try(() => ReportDescriptorReader.ReadApplicationUsages(device.GetRawReportDescriptor()), []));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			return null;
		}
	}

	/// <summary>Строки (производитель, имя, серийный номер) и дескриптор читаются не всегда: без них устройство остаётся в списке.</summary>
	private static T? Try<T>(Func<T> read, T? fallback = default)
	{
		try
		{
			return read();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
		{
			return fallback;
		}
	}

	private sealed class Subscription(Action unsubscribe) : IDisposable
	{
		public void Dispose() => unsubscribe();
	}
}

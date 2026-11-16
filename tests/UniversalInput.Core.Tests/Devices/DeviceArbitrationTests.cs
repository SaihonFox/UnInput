using UniversalInput.Core.Configuration;
using UniversalInput.Core.Devices;
using UniversalInput.Testing.Mock;

namespace UniversalInput.Core.Tests.Devices;

public class DeviceArbitrationTests
{
	private static readonly TimeSpan Retention = TimeSpan.FromMinutes(5);

	private static DeviceDescriptor Pad(string key, string? serial = "SN-1", DeviceBus bus = DeviceBus.Usb) =>
		MockInputBackend.Describe(key, InputDeviceKind.Gamepad) with
		{
			Bus = bus,
			Identity = new DeviceIdentity(VendorId: 0x045E, ProductId: 0x028E, SerialNumber: serial),
		};

	[Fact]
	public void SameHardwareFromTwoBackends_BecomesOneLogicalDevice()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);

		var first = registry.Add(0, "hid", Pad("hid:1"), out var firstCreated);
		var second = registry.Add(1, "sdl", Pad("sdl:1"), out var secondCreated);

		Assert.True(firstCreated);
		Assert.False(secondCreated);
		Assert.Equal(first.Id, second.Id);
		var device = Assert.Single(registry.Snapshot);
		Assert.Equal<string>(["sdl", "hid"], device.Providers);
		Assert.Equal("sdl", device.BackendId);
	}

	[Fact]
	public void EachRoleGoesToItsOwnPreferredBackend()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);
		registry.Add(0, "hid", Pad("hid:1"));
		registry.Add(1, "sdl", Pad("sdl:1"));

		var owners = Assert.Single(registry.Snapshot).RoleOwners;

		Assert.Equal("sdl", owners[InputOperation.Observe]);
		Assert.Equal("sdl", owners[InputOperation.Emulate]);
		Assert.Equal("hid", owners[InputOperation.DirectAccess]);
		Assert.False(owners.ContainsKey(InputOperation.Output));
	}

	[Fact]
	public void ConfiguredPriority_OverridesTheDefaults()
	{
		var options = new InputHubOptions();
		options.KindPriority["gamepad"] = ["hid", "sdl"];
		var registry = new DeviceRegistry(TimeProvider.System, Retention, new RolePriority(options));
		registry.Add(0, "hid", Pad("hid:1"));
		registry.Add(1, "sdl", Pad("sdl:1"));

		var device = Assert.Single(registry.Snapshot);

		Assert.Equal("hid", device.BackendId);
		Assert.Equal("hid", device.RoleOwners[InputOperation.Observe]);
	}

	[Fact]
	public void ARoleGoesOnlyToABackendThatSupportsIt()
	{
		var withoutEmulation = Pad("sdl:1") with
		{
			Operations = new Dictionary<InputOperation, OperationSupport>
			{
				[InputOperation.Enumerate] = OperationSupport.Supported,
				[InputOperation.Observe] = OperationSupport.Supported,
				[InputOperation.Emulate] = OperationSupport.Unsupported("нет"),
			},
		};
		var registry = new DeviceRegistry(TimeProvider.System, Retention);
		registry.Add(0, "hid", Pad("hid:1"));
		registry.Add(1, "sdl", withoutEmulation);

		var owners = Assert.Single(registry.Snapshot).RoleOwners;

		Assert.Equal("sdl", owners[InputOperation.Observe]);
		Assert.Equal("hid", owners[InputOperation.Emulate]);
	}

	[Theory]
	[InlineData("SN-2", DeviceBus.Usb)]
	[InlineData("SN-1", DeviceBus.Bluetooth)]
	[InlineData(null, DeviceBus.Usb)]
	public void DifferentOrUnknownHardware_IsNotMerged(string? serial, DeviceBus bus)
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);
		registry.Add(0, "hid", Pad("hid:1"));

		registry.Add(1, "sdl", Pad("sdl:1", serial, bus));

		Assert.Equal(2, registry.Snapshot.Count);
	}

	[Fact]
	public void TwoDevicesFromTheSameBackend_AreNeverMerged()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);

		registry.Add(0, "hid", Pad("hid:1"));
		registry.Add(0, "hid", Pad("hid:2"));

		Assert.Equal(2, registry.Snapshot.Count);
	}

	[Fact]
	public void WhenTheOwnerLeaves_TheDeviceStaysAndTheRolesMove()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);
		var id = registry.Add(0, "hid", Pad("hid:1")).Id;
		registry.Add(1, "sdl", Pad("sdl:1"));

		Assert.True(registry.Remove(id, 1, out _, out var gone));

		Assert.False(gone);
		var device = Assert.Single(registry.Snapshot);
		Assert.Equal("hid", device.BackendId);
		Assert.Equal("hid", device.RoleOwners[InputOperation.Observe]);
		Assert.Equal<string>(["hid"], device.Providers);
	}

	[Fact]
	public void WhenTheLastProviderLeaves_TheDeviceIsGone()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);
		var id = registry.Add(0, "hid", Pad("hid:1")).Id;
		registry.Add(1, "sdl", Pad("sdl:1"));
		registry.Remove(id, 1, out _, out _);

		Assert.True(registry.Remove(id, 0, out _, out var gone));

		Assert.True(gone);
		Assert.Empty(registry.Snapshot);
	}

	[Fact]
	public void RemoveAll_ReportsGoneDevicesAndSurvivorsSeparately()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);
		var shared = registry.Add(0, "hid", Pad("hid:1")).Id;
		registry.Add(1, "sdl", Pad("sdl:1"));
		var alone = registry.Add(1, "sdl", Pad("sdl:2", "SN-9")).Id;
		List<DeviceId> survivors = [];

		var gone = registry.RemoveAll(1, survivors);

		Assert.Equal<DeviceId>([alone], gone.Select(d => d.Id));
		Assert.Equal<DeviceId>([shared], survivors);
		Assert.Equal<DeviceId>([shared], registry.Snapshot.Select(d => d.Id));
	}

	[Fact]
	public void ADeviceThatReturnsAfterRemoval_KeepsItsId_AndStillMerges()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);
		var id = registry.Add(0, "hid", Pad("hid:1")).Id;
		registry.Add(1, "sdl", Pad("sdl:1"));
		registry.RemoveAll(0);
		registry.RemoveAll(1);

		var back = registry.Add(1, "sdl", Pad("sdl:1"), out var created);
		var joined = registry.Add(0, "hid", Pad("hid:1"), out var joinedCreated);

		Assert.True(created);
		Assert.False(joinedCreated);
		Assert.Equal(id, back.Id);
		Assert.Equal(id, joined.Id);
		Assert.Single(registry.Snapshot);
	}

	[Fact]
	public void KeyboardObserved_IsTrueOnlyForPerDeviceKeyboardsThatCanObserve()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);
		registry.Add(0, "hook", MockInputBackend.Describe("system", InputDeviceKind.SystemWide, origin: DeviceOrigin.Aggregate));
		Assert.False(registry.KeyboardObserved);
		Assert.False(registry.MouseObserved);

		var keyboard = registry.Add(1, "native", MockInputBackend.Describe("kbd", InputDeviceKind.Keyboard));
		Assert.True(registry.KeyboardObserved);
		Assert.False(registry.MouseObserved);

		registry.Remove(keyboard.Id, 1, out _);
		Assert.False(registry.KeyboardObserved);
	}
}

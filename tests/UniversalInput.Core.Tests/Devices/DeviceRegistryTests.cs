using UniversalInput.Core.Devices;
using UniversalInput.Testing.Harness;
using UniversalInput.Testing.Mock;

namespace UniversalInput.Core.Tests.Devices;

public class DeviceRegistryTests
{
	private static readonly TimeSpan Retention = TimeSpan.FromMinutes(5);

	private static DeviceDescriptor Describe(string key) => MockInputBackend.Describe(key, InputDeviceKind.Keyboard);

	[Fact]
	public void Add_AssignsSequentialIds_AndPublishesThemInTheSnapshot()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);

		var first = registry.Add(0, "a", Describe("one"));
		var second = registry.Add(0, "a", Describe("two"));

		Assert.Equal(new DeviceId(1), first.Id);
		Assert.Equal(new DeviceId(2), second.Id);
		Assert.Equal<DeviceId>([first.Id, second.Id], registry.Snapshot.Select(d => d.Id));
		Assert.Equal("a", first.BackendId);
	}

	[Fact]
	public void Add_SameKeyWhileConnected_ThrowsDeviceBusy()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);
		registry.Add(0, "a", Describe("one"));

		var error = Assert.Throws<InputException>(() => registry.Add(1, "b", Describe("one")));

		Assert.Equal(InputErrorCode.DeviceBusy, error.Code);
	}

	[Fact]
	public void Remove_HidesTheDevice_ButKeepsTheEntryForLookups()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);
		var device = registry.Add(0, "a", Describe("one"));

		Assert.True(registry.Remove(device.Id, 0, out var removed));

		Assert.Equal(device, removed);
		Assert.Empty(registry.Snapshot);
		Assert.False(registry.TryGetLive(device.Id, out _));
		Assert.True(registry.TryGet(device.Id, out var entry));
		Assert.False(entry.Live);
	}

	[Fact]
	public void Remove_FromAnotherBackend_IsRejected()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);
		var device = registry.Add(0, "a", Describe("one"));

		Assert.False(registry.Remove(device.Id, 1, out _));
		Assert.Single(registry.Snapshot);
	}

	[Fact]
	public void Remove_Twice_ReturnsFalseTheSecondTime()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);
		var device = registry.Add(0, "a", Describe("one"));

		Assert.True(registry.Remove(device.Id, 0, out _));
		Assert.False(registry.Remove(device.Id, 0, out _));
	}

	[Fact]
	public void Reconnect_WithinRetention_KeepsTheSameId()
	{
		var time = new ManualTimeProvider();
		var registry = new DeviceRegistry(time, Retention);
		var device = registry.Add(0, "a", Describe("one"));
		registry.Remove(device.Id, 0, out _);

		time.Advance(TimeSpan.FromMinutes(4));
		var again = registry.Add(0, "a", Describe("one"));

		Assert.Equal(device.Id, again.Id);
		Assert.True(registry.TryGetLive(again.Id, out _));
	}

	[Fact]
	public void Reconnect_AfterRetention_GetsANewId()
	{
		var time = new ManualTimeProvider();
		var registry = new DeviceRegistry(time, Retention);
		var device = registry.Add(0, "a", Describe("one"));
		registry.Remove(device.Id, 0, out _);

		time.Advance(TimeSpan.FromMinutes(6));
		var again = registry.Add(0, "a", Describe("one"));

		Assert.NotEqual(device.Id, again.Id);
		Assert.False(registry.TryGet(device.Id, out _));
	}

	[Fact]
	public void Reconnect_ThroughAnotherBackend_IsAllowedAfterRemoval()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);
		var device = registry.Add(0, "a", Describe("one"));
		registry.Remove(device.Id, 0, out _);

		var again = registry.Add(1, "b", Describe("one"));

		Assert.Equal(device.Id, again.Id);
		Assert.Equal("b", again.BackendId);
	}

	[Fact]
	public void RemoveAll_AffectsOnlyTheGivenBackend()
	{
		var registry = new DeviceRegistry(TimeProvider.System, Retention);
		var a1 = registry.Add(0, "a", Describe("a1"));
		var a2 = registry.Add(0, "a", Describe("a2"));
		var b1 = registry.Add(1, "b", Describe("b1"));

		var removed = registry.RemoveAll(0);

		Assert.Equal<DeviceId>([a1.Id, a2.Id], removed.Select(d => d.Id).OrderBy(id => id.Value));
		Assert.Equal<DeviceId>([b1.Id], registry.Snapshot.Select(d => d.Id));
	}
}

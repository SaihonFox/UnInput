using UniversalInput.Backends.Hid.Extensions;

namespace UniversalInput.Backends.Hid.Tests;

public class HidBackendBuilderExtensionsTests
{
	private sealed class RecordingBuilder : IInputBackendBuilder
	{
		public Func<IServiceProvider, IInputBackend>? Factory { get; private set; }

		public bool Required { get; private set; }

		public IInputBackendBuilder AddBackend(Func<IServiceProvider, IInputBackend> factory, bool required = false)
		{
			Factory = factory;
			Required = required;
			return this;
		}
	}

	[Fact]
	public void AddHid_RegistersTheBackend_AndAppliesTheConfiguration()
	{
		var builder = new RecordingBuilder();
		var configured = false;

		var result = builder.AddHid(options =>
		{
			configured = true;
			options.RescanInterval = TimeSpan.FromSeconds(3);
		}, required: true);

		Assert.Same(builder, result);
		Assert.True(builder.Required);
		var backend = Assert.IsType<HidBackend>(builder.Factory!(null!));
		Assert.Equal(HidBackend.BackendId, backend.Id);
		Assert.True(configured);
	}

	[Fact]
	public void AddHid_RejectsNullBuilder() =>
		Assert.Throws<ArgumentNullException>(() => HidBackendBuilderExtensions.AddHid(null!));
}

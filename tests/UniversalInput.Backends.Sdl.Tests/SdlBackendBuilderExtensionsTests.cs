using UniversalInput.Backends.Sdl.Extensions;

namespace UniversalInput.Backends.Sdl.Tests;

public class SdlBackendBuilderExtensionsTests
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
	public void AddSdl_RegistersTheBackend_AndAppliesTheConfiguration()
	{
		var builder = new RecordingBuilder();
		var configured = false;

		var result = builder.AddSdl(options =>
		{
			configured = true;
			options.EnableSensors = true;
		}, required: true);

		Assert.Same(builder, result);
		Assert.True(builder.Required);
		var backend = Assert.IsType<SdlBackend>(builder.Factory!(null!));
		Assert.Equal(SdlBackend.BackendId, backend.Id);
		Assert.True(configured);
	}

	[Fact]
	public void AddSdl_RejectsNullBuilder() =>
		Assert.Throws<ArgumentNullException>(() => SdlBackendBuilderExtensions.AddSdl(null!));
}

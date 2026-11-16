using UniversalInput.Backends.GlobalHook.Extensions;

namespace UniversalInput.Backends.GlobalHook.Tests;

public class GlobalHookBackendBuilderExtensionsTests
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
	public void AddGlobalHook_RegistersTheBackend()
	{
		var builder = new RecordingBuilder();

		var result = builder.AddGlobalHook(required: true);

		Assert.Same(builder, result);
		Assert.True(builder.Required);
		var backend = Assert.IsType<GlobalHookBackend>(builder.Factory!(null!));
		Assert.Equal(GlobalHookBackend.BackendId, backend.Id);
	}

	[Fact]
	public void AddGlobalHook_RejectsNullBuilder() =>
		Assert.Throws<ArgumentNullException>(() => GlobalHookBackendBuilderExtensions.AddGlobalHook(null!));
}

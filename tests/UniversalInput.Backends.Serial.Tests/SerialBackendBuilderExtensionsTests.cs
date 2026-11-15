using UniversalInput.Backends.Serial.Extensions;
using UniversalInput.Backends.Serial.Options;

namespace UniversalInput.Backends.Serial.Tests;

public class SerialBackendBuilderExtensionsTests
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
	public void AddSerial_RegistersASerialBackend()
	{
		var builder = new RecordingBuilder();

		var result = builder.AddSerial(required: true);

		Assert.Same(builder, result);
		Assert.True(builder.Required);
		var backend = Assert.IsType<SerialBackend>(builder.Factory!(null!));
		Assert.Equal(SerialBackend.BackendId, backend.Id);
	}

	[Fact]
	public void AddSerial_AppliesTheConfiguration_AndCreatesABackendPerCall()
	{
		var builder = new RecordingBuilder();
		var seen = new List<TimeSpan>();

		builder.AddSerial(options =>
		{
			options.PollInterval = TimeSpan.FromSeconds(7);
			seen.Add(options.PollInterval);
		});
		var first = builder.Factory!(null!);
		var second = builder.Factory!(null!);

		Assert.Equal<TimeSpan>([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7)], seen);
		Assert.NotSame(first, second);
	}

	[Fact]
	public void AddSerial_RejectsInvalidOptionsWhenTheBackendIsCreated()
	{
		var builder = new RecordingBuilder();
		builder.AddSerial(options => options.DataBits = 9);

		Assert.Throws<ArgumentOutOfRangeException>(() => builder.Factory!(null!));
	}

	[Fact]
	public void AddSerial_RejectsNullBuilder() =>
		Assert.Throws<ArgumentNullException>(() => SerialBackendBuilderExtensions.AddSerial(null!));
}

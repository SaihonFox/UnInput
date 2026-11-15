using UniversalInput.Core.Backends;
using UniversalInput.Core.Configuration;
using UniversalInput.Core.Hub;

namespace UniversalInput.Testing.Harness;

/// <summary>Быстрая сборка хаба для тестов: короткие задержки перезапуска и остановки.</summary>
public static class TestInputHub
{
	public static InputHubOptions FastOptions() => new()
	{
		RestartMinDelay = TimeSpan.FromMilliseconds(10),
		RestartMaxDelay = TimeSpan.FromMilliseconds(80),
		BackendStopTimeout = TimeSpan.FromMilliseconds(300),
	};

	public static InputHub Create(params IInputBackend[] backends) => Create(FastOptions(), backends);

	public static InputHub Create(InputHubOptions options, params IInputBackend[] backends) =>
		new(backends.Select(b => new BackendRegistration(b)), options);
}

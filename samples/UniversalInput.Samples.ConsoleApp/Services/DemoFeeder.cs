using Microsoft.Extensions.Hosting;
using UniversalInput.Testing.Mock;

namespace UniversalInput.Samples.ConsoleApp.Services;

/// <summary>Имитирует ввод через mock backend: набирает «HELLO», двигает мышь и завершает приложение.</summary>
internal sealed class DemoFeeder : BackgroundService
{
	// Коды USB HID Usage для букв H, E, L, L, O.
	private static readonly int[] Hello = [0x0B, 0x08, 0x0F, 0x0F, 0x12];

	private readonly MockInputBackend _backend;
	private readonly IHostApplicationLifetime _lifetime;

	public DemoFeeder(MockInputBackend backend, IHostApplicationLifetime lifetime)
	{
		_backend = backend;
		_lifetime = lifetime;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			var keyboard = _backend.IdOf("demo-keyboard");
			var mouse = _backend.IdOf("demo-mouse");

			foreach (var usage in Hello)
			{
				_backend.Emit(InputEvent.KeyDown(keyboard, usage));
				_backend.Emit(InputEvent.KeyUp(keyboard, usage));
				await Task.Delay(150, stoppingToken);
			}

			for (var i = 0; i < 3; i++)
			{
				_backend.Emit(InputEvent.MouseMove(mouse, 5, -2));
				await Task.Delay(100, stoppingToken);
			}

			_backend.Emit(InputEvent.MouseDown(mouse, (int)MouseButton.Left));
			_backend.Emit(InputEvent.MouseUp(mouse, (int)MouseButton.Left));

			await Task.Delay(300, stoppingToken);
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			_lifetime.StopApplication();
		}
	}
}

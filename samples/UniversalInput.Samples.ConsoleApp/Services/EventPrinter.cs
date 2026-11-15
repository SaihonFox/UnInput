using Microsoft.Extensions.Hosting;

namespace UniversalInput.Samples.ConsoleApp.Services;

/// <summary>Подписывается на события хаба и печатает их в консоль.</summary>
internal sealed class EventPrinter : BackgroundService
{
	private readonly IInputHub _hub;
	private IInputSubscription? _subscription;

	public EventPrinter(IInputHub hub) => _hub = hub;

	public override Task StartAsync(CancellationToken cancellationToken)
	{
		// Подписываемся до того, как источник начнёт отправлять события.
		_subscription = _hub.Subscribe();
		Console.WriteLine("Операции:");
		foreach (var operation in Enum.GetValues<InputOperation>())
		{
			var support = _hub.GetSupport(operation);
			Console.WriteLine($"  {operation,-12} {support.State}{(support.Reason is null ? string.Empty : " — " + support.Reason)}");
		}

		return base.StartAsync(cancellationToken);
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await foreach (var inputEvent in _subscription!.Reader.ReadAllAsync(stoppingToken))
			{
				Console.WriteLine(Describe(inputEvent));
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	public override void Dispose()
	{
		_subscription?.DisposeAsync().AsTask().GetAwaiter().GetResult();
		base.Dispose();
	}

	private string Describe(InputEvent inputEvent)
	{
		var device = _hub.Devices.FirstOrDefault(d => d.Id == inputEvent.Device)?.Name ?? inputEvent.Device.ToString();
		var details = inputEvent.Kind switch
		{
			InputEventKind.KeyPressed or InputEventKind.KeyReleased => $"usage 0x{inputEvent.AsKey().Usage:X2}",
			InputEventKind.MouseMoved => $"dx={inputEvent.AsPointer().X}, dy={inputEvent.AsPointer().Y}",
			InputEventKind.MouseButtonPressed or InputEventKind.MouseButtonReleased => $"button {inputEvent.AsMouseButton().Button}",
			InputEventKind.StreamGap => $"потеряно {inputEvent.AsStreamGap().Lost}: {inputEvent.AsStreamGap().Reason}",
			_ => string.Empty,
		};

		return $"#{inputEvent.Sequence,-3} {inputEvent.Kind,-20} {device,-14} {details}";
	}
}

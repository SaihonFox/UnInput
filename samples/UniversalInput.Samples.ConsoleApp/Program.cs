using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UniversalInput.Hosting;
using UniversalInput.Samples.ConsoleApp.Services;
using UniversalInput.Testing.Mock;

// Пример работает на mock backend: настоящих устройств библиотека на этом этапе ещё не читает.
// Он показывает форму API: AddUniversalInput, подписку на события и проверку возможностей.
var demo = new MockInputBackend("demo");
demo.InitialDevices.Add(MockInputBackend.Describe("demo-keyboard", InputDeviceKind.Keyboard));
demo.InitialDevices.Add(MockInputBackend.Describe(
	"demo-mouse",
	InputDeviceKind.Mouse,
	InputCapabilities.PointerMotion | InputCapabilities.PointerButtons));

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddUniversalInput(input => input.AddBackend(_ => demo));

// Службы хоста стартуют в порядке регистрации: хаб, затем подписка, затем источник событий.
builder.Services.AddHostedService<EventPrinter>();
builder.Services.AddHostedService(provider => new DemoFeeder(
	demo, provider.GetRequiredService<IHostApplicationLifetime>()));

using var host = builder.Build();
await host.RunAsync();

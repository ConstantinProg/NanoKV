using NanoKV.Core.Storage;
using NanoKV.Server;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var resourceBuilder = ResourceBuilder
    .CreateDefault()
    .AddService(Telemetry.ServiceName);

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddSource(Telemetry.ServiceName)
    .AddConsoleExporter()
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddMeter(Telemetry.ServiceName)
    .AddConsoleExporter()
    .Build();

using var store = new SimpleStore();
var handler = new StoreCommandHandler(store);

var server = new TcpServer("127.0.0.1", 8080, handler);
using var cts = new CancellationTokenSource();

var serverTask = server.StartAsync(cts.Token);

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine("Server started. Press Ctrl+C to stop.");

await serverTask;
using Microsoft.Extensions.Logging;
using NanoKV.Core.Storage;
using NanoKV.Server;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var options = new NanoKvServerOptions
{
    Host = "127.0.0.1",
    Port = 8080,
    MaxConcurrentConnections = 100,
    MaxCommandBytes = 4 * 1024,
    ReceiveBufferSize = 4 * 1024,
    ListenBacklog = 100,
    IdleTimeout = TimeSpan.FromSeconds(30),
    ShutdownTimeout = TimeSpan.FromSeconds(5)
};

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(LogLevel.Information)
        .AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.TimestampFormat = "HH:mm:ss ";
        });
});

ILogger<Program> logger = loggerFactory.CreateLogger<Program>();

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

await using var server = new TcpServer(
    options,
    handler,
    loggerFactory.CreateLogger<TcpServer>());

using var shutdownCts = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;

    logger.LogInformation("Ctrl+C received. Graceful shutdown requested.");

    shutdownCts.Cancel();
};

await server.StartAsync(shutdownCts.Token);

logger.LogInformation(
    "Server started. Press Ctrl+C to stop.");

try
{
    await Task.Delay(Timeout.InfiniteTimeSpan, shutdownCts.Token);
}
catch (OperationCanceledException)
{
    // Expected on Ctrl+C.
}

using var stopCts = new CancellationTokenSource(options.ShutdownTimeout);

try
{
    await server.StopAsync(stopCts.Token);
}
catch (OperationCanceledException)
{
    logger.LogWarning(
        "Graceful shutdown timeout expired after {ShutdownTimeout}.",
        options.ShutdownTimeout);
}
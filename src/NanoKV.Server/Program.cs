using Microsoft.Extensions.Logging;
using NanoKV.Core.Storage;
using NanoKV.Server;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

NanoKvServerOptions serverOptions = CreateServerOptions(args);

bool loggingEnabled = IsEnabled(
    args,
    argumentName: "--logging",
    environmentVariableName: "NANOKV_LOGGING",
    defaultValue: false);

bool telemetryEnabled = IsEnabled(
    args,
    argumentName: "--telemetry",
    environmentVariableName: "NANOKV_TELEMETRY",
    defaultValue: false);

using ILoggerFactory loggerFactory = CreateLoggerFactory(loggingEnabled);

ILogger<Program> logger = loggerFactory.CreateLogger<Program>();

ResourceBuilder resourceBuilder = ResourceBuilder
    .CreateDefault()
    .AddService(Telemetry.ServiceName);

TracerProvider? tracerProvider = null;
MeterProvider? meterProvider = null;

if (telemetryEnabled)
{
    tracerProvider = Sdk.CreateTracerProviderBuilder()
        .SetResourceBuilder(resourceBuilder)
        .AddSource(Telemetry.ServiceName)
        .AddConsoleExporter()
        .Build();

    meterProvider = Sdk.CreateMeterProviderBuilder()
        .SetResourceBuilder(resourceBuilder)
        .AddMeter(Telemetry.ServiceName)
        .AddConsoleExporter()
        .Build();

    logger.LogInformation("OpenTelemetry is enabled.");
}
else
{
    logger.LogInformation("OpenTelemetry is disabled.");
}

using (tracerProvider)
using (meterProvider)
using (var store = new SimpleStore())
{
    var handler = new StoreCommandHandler(store);

    await using var server = new TcpServer(
        serverOptions,
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
        "NanoKV server started on {Host}:{Port}. Press Ctrl+C to stop.",
        serverOptions.Host,
        serverOptions.Port);

    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, shutdownCts.Token);
    }
    catch (OperationCanceledException)
    {
        // Expected on Ctrl+C.
    }

    using var stopCts =
        new CancellationTokenSource(serverOptions.ShutdownTimeout);

    try
    {
        await server.StopAsync(stopCts.Token);
    }
    catch (OperationCanceledException)
    {
        logger.LogWarning(
            "Graceful shutdown timeout expired after {ShutdownTimeout}.",
            serverOptions.ShutdownTimeout);
    }
}

static ILoggerFactory CreateLoggerFactory(bool loggingEnabled)
{
    return LoggerFactory.Create(builder =>
    {
        if (!loggingEnabled)
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.None);
            return;
        }

        builder
            .SetMinimumLevel(LogLevel.Information)
            .AddSimpleConsole(console =>
            {
                console.SingleLine = true;
                console.TimestampFormat = "HH:mm:ss ";
            });
    });
}

static NanoKvServerOptions CreateServerOptions(string[] args)
{
    return new NanoKvServerOptions
    {
        Host = GetString(
            args,
            argumentName: "--host",
            environmentVariableName: "NANOKV_HOST",
            defaultValue: "127.0.0.1"),

        Port = GetInt(
            args,
            argumentName: "--port",
            environmentVariableName: "NANOKV_PORT",
            defaultValue: 8080),

        MaxConcurrentConnections = GetInt(
            args,
            argumentName: "--max-connections",
            environmentVariableName: "NANOKV_MAX_CONNECTIONS",
            defaultValue: 100),

        MaxCommandBytes = GetInt(
            args,
            argumentName: "--max-command-bytes",
            environmentVariableName: "NANOKV_MAX_COMMAND_BYTES",
            defaultValue: 4 * 1024),

        ReceiveBufferSize = GetInt(
            args,
            argumentName: "--receive-buffer-size",
            environmentVariableName: "NANOKV_RECEIVE_BUFFER_SIZE",
            defaultValue: 4 * 1024),

        ListenBacklog = GetInt(
            args,
            argumentName: "--listen-backlog",
            environmentVariableName: "NANOKV_LISTEN_BACKLOG",
            defaultValue: 100),

        IdleTimeout = TimeSpan.FromSeconds(
            GetInt(
                args,
                argumentName: "--idle-timeout-seconds",
                environmentVariableName: "NANOKV_IDLE_TIMEOUT_SECONDS",
                defaultValue: 30)),

        ShutdownTimeout = TimeSpan.FromSeconds(
            GetInt(
                args,
                argumentName: "--shutdown-timeout-seconds",
                environmentVariableName: "NANOKV_SHUTDOWN_TIMEOUT_SECONDS",
                defaultValue: 5))
    };
}

static bool IsEnabled(
    string[] args,
    string argumentName,
    string environmentVariableName,
    bool defaultValue)
{
    string? argumentValue = GetArgumentValue(args, argumentName);

    if (argumentValue is not null)
        return ParseBool(argumentValue, argumentName);

    string? environmentValue =
        Environment.GetEnvironmentVariable(environmentVariableName);

    if (!string.IsNullOrWhiteSpace(environmentValue))
        return ParseBool(environmentValue, environmentVariableName);

    return defaultValue;
}

static string GetString(
    string[] args,
    string argumentName,
    string environmentVariableName,
    string defaultValue)
{
    string? argumentValue = GetArgumentValue(args, argumentName);

    if (!string.IsNullOrWhiteSpace(argumentValue))
        return argumentValue;

    string? environmentValue =
        Environment.GetEnvironmentVariable(environmentVariableName);

    if (!string.IsNullOrWhiteSpace(environmentValue))
        return environmentValue;

    return defaultValue;
}

static int GetInt(
    string[] args,
    string argumentName,
    string environmentVariableName,
    int defaultValue)
{
    string? argumentValue = GetArgumentValue(args, argumentName);

    if (!string.IsNullOrWhiteSpace(argumentValue))
        return ParsePositiveInt(argumentValue, argumentName);

    string? environmentValue =
        Environment.GetEnvironmentVariable(environmentVariableName);

    if (!string.IsNullOrWhiteSpace(environmentValue))
        return ParsePositiveInt(environmentValue, environmentVariableName);

    return defaultValue;
}

static string? GetArgumentValue(string[] args, string name)
{
    string prefix = name + "=";

    foreach (string arg in args)
    {
        if (arg.Equals(name, StringComparison.OrdinalIgnoreCase))
            return "true";

        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return arg[prefix.Length..];
    }

    return null;
}

static int ParsePositiveInt(string value, string sourceName)
{
    if (!int.TryParse(value, out int result) || result <= 0)
        throw new ArgumentException($"{sourceName} must be a positive integer.");

    return result;
}

static bool ParseBool(string value, string sourceName)
{
    return value.Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "yes" or "on" or "enabled" => true,
        "0" or "false" or "no" or "off" or "disabled" => false,
        _ => throw new ArgumentException($"{sourceName} must be true or false.")
    };
}
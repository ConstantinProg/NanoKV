namespace NanoKV.Server;

public sealed class NanoKvServerOptions
{
    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 8080;

    public int MaxConcurrentConnections { get; init; } = 100;

    public int MaxCommandBytes { get; init; } = 4 * 1024;

    public int ReceiveBufferSize { get; init; } = 4 * 1024;

    public int ListenBacklog { get; init; } = 100;

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public NanoKvTelemetryOptions Telemetry { get; init; } = new();
}
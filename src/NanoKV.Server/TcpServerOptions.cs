namespace NanoKV.Server;

public sealed class TcpServerOptions
{
    public int MaxConcurrentConnections { get; init; } = 100;

    public int MaxCommandBytes { get; init; } = 4 * 1024;

    public int ReceiveBufferSize { get; init; } = 4 * 1024;

    public int ListenBacklog { get; init; } = 100;

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
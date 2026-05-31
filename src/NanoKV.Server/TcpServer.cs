using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NanoKV.Core.Protocol;

namespace NanoKV.Server;

public sealed class TcpServer
{
    private static readonly TimeSpan ProtocolErrorDrainTimeout =
        TimeSpan.FromMilliseconds(200);

    private readonly IPEndPoint _endpoint;
    private readonly SemaphoreSlim _connectionLimiter;
    private readonly ICommandHandler _commandHandler;
    private readonly TcpServerOptions _options;

    public TcpServer(string ip, int port, ICommandHandler handler)
        : this(ip, port, handler, new TcpServerOptions())
    {
    }

    public TcpServer(
        string ip,
        int port,
        ICommandHandler handler,
        TcpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(options);

        ValidateOptions(options);

        _endpoint = new IPEndPoint(IPAddress.Parse(ip), port);
        _commandHandler = handler;
        _options = options;
        _connectionLimiter = new SemaphoreSlim(options.MaxConcurrentConnections);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var server = new Socket(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp);

        server.Bind(_endpoint);
        server.Listen(_options.ListenBacklog);

        while (!cancellationToken.IsCancellationRequested)
        {
            Socket client;

            try
            {
                client = await server.AcceptAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!_connectionLimiter.Wait(0))
            {
                _ = Task.Run(
                    () => RejectClientAsync(client),
                    CancellationToken.None);

                continue;
            }

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await ProcessClientAsync(client, cancellationToken);
                    }
                    finally
                    {
                        _connectionLimiter.Release();
                    }
                },
                CancellationToken.None);
        }
    }

    private static async Task RejectClientAsync(Socket client)
    {
        try
        {
            await client.SendAsync(
                ProtocolResponse.Error("too many connections"),
                CancellationToken.None);

            await GracefulProtocolCloseAsync(client, CancellationToken.None);
        }
        catch
        {
            CloseClient(client);
        }
    }

    private async Task ProcessClientAsync(
        Socket client,
        CancellationToken serverCancellationToken)
    {
        byte[] receiveBuffer = ArrayPool<byte>.Shared.Rent(_options.ReceiveBufferSize);

        using var lineBuffer = new LineBuffer(_options.MaxCommandBytes);

        try
        {
            while (!serverCancellationToken.IsCancellationRequested)
            {
                int bytesRead;

                try
                {
                    using var idleCts =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            serverCancellationToken);

                    idleCts.CancelAfter(_options.IdleTimeout);

                    bytesRead = await client.ReceiveAsync(
                        receiveBuffer.AsMemory(0, _options.ReceiveBufferSize),
                        idleCts.Token);
                }
                catch (OperationCanceledException)
                    when (!serverCancellationToken.IsCancellationRequested)
                {
                    await client.SendAsync(
                        ProtocolResponse.Error("idle timeout"),
                        CancellationToken.None);

                    await GracefulProtocolCloseAsync(
                        client,
                        CancellationToken.None);

                    return;
                }

                if (bytesRead == 0)
                    return;

                ProcessReceivedBytesResult result = ProcessReceivedBytes(
                    receiveBuffer.AsSpan(0, bytesRead),
                    lineBuffer,
                    client.RemoteEndPoint);

                foreach (byte[] response in result.Responses)
                {
                    await client.SendAsync(response, serverCancellationToken);
                }

                if (!result.ContinueConnection)
                {
                    if (result.CloseAfterSendingResponses)
                    {
                        await GracefulProtocolCloseAsync(
                            client,
                            CancellationToken.None);
                    }

                    return;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(receiveBuffer);
            CloseClient(client);
        }
    }

    private ProcessReceivedBytesResult ProcessReceivedBytes(
        ReadOnlySpan<byte> received,
        LineBuffer lineBuffer,
        EndPoint remoteEndPoint)
    {
        var responses = new List<byte[]>();

        int offset = 0;

        while (offset < received.Length)
        {
            ReadOnlySpan<byte> remaining = received[offset..];

            int newlineIndex = remaining.IndexOf((byte)'\n');

            if (newlineIndex < 0)
            {
                if (!lineBuffer.TryAppend(remaining))
                {
                    responses.Add(ProtocolResponse.Error("command too long"));

                    return new ProcessReceivedBytesResult(
                        ContinueConnection: false,
                        CloseAfterSendingResponses: true,
                        Responses: responses);
                }

                return new ProcessReceivedBytesResult(
                    ContinueConnection: true,
                    CloseAfterSendingResponses: false,
                    Responses: responses);
            }

            ReadOnlySpan<byte> segmentBeforeNewline = remaining[..newlineIndex];

            if (!lineBuffer.TryAppend(segmentBeforeNewline))
            {
                responses.Add(ProtocolResponse.Error("command too long"));

                return new ProcessReceivedBytesResult(
                    ContinueConnection: false,
                    CloseAfterSendingResponses: true,
                    Responses: responses);
            }

            byte[] response = ProcessCommand(
                lineBuffer.WrittenSpan,
                remoteEndPoint);

            lineBuffer.Clear();

            if (response.Length > 0)
                responses.Add(response);

            offset += newlineIndex + 1;
        }

        return new ProcessReceivedBytesResult(
            ContinueConnection: true,
            CloseAfterSendingResponses: false,
            Responses: responses);
    }

    private byte[] ProcessCommand(
        ReadOnlySpan<byte> line,
        EndPoint remoteEndPoint)
    {
        ParsedCommand command = CommandParser.Parse(line);

        if (command.IsEmpty)
            return ProtocolResponse.Error("empty command");

        string commandName = GetCommandName(command.Type);

        using Activity activity = Telemetry.ActivitySource.StartActivity(
            "Process command",
            ActivityKind.Server);

        activity?.SetTag("command.name", commandName);
        activity?.SetTag("command.has_key", !command.Key.IsEmpty);
        activity?.SetTag("net.peer", remoteEndPoint.ToString());

        Stopwatch stopwatch = Stopwatch.StartNew();

        byte[] response = _commandHandler.Handle(command);

        stopwatch.Stop();

        var tags = new TagList
        {
            { "command.name", commandName },
            { "command.has_key", !command.Key.IsEmpty }
        };

        Telemetry.CommandsProcessed.Add(1, tags);
        Telemetry.CommandDuration.Record(
            stopwatch.Elapsed.TotalMilliseconds,
            tags);

        return response;
    }

    private static async Task GracefulProtocolCloseAsync(
        Socket client,
        CancellationToken cancellationToken)
    {
        try
        {
            client.Shutdown(SocketShutdown.Send);
        }
        catch
        {
            CloseClient(client);
            return;
        }

        byte[] drainBuffer = ArrayPool<byte>.Shared.Rent(512);

        try
        {
            using var drainCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            drainCts.CancelAfter(ProtocolErrorDrainTimeout);

            while (!drainCts.IsCancellationRequested)
            {
                int read = await client.ReceiveAsync(
                    drainBuffer.AsMemory(0, drainBuffer.Length),
                    drainCts.Token);

                if (read == 0)
                    break;
            }
        }
        catch
        {
            // Best-effort drain only.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(drainBuffer);
            CloseClient(client);
        }
    }

    private static void CloseClient(Socket client)
    {
        try
        {
            client.Shutdown(SocketShutdown.Both);
        }
        catch
        {
            // Connection may already be closed.
        }

        client.Dispose();
    }

    private static string GetCommandName(CommandType type)
    {
        return type switch
        {
            CommandType.Set => "SET",
            CommandType.Get => "GET",
            CommandType.Delete => "DELETE",
            CommandType.Stats => "STATS",
            CommandType.Unknown => "UNKNOWN",
            _ => "UNKNOWN"
        };
    }

    private static void ValidateOptions(TcpServerOptions options)
    {
        if (options.MaxConcurrentConnections <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxConcurrentConnections));

        if (options.MaxCommandBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxCommandBytes));

        if (options.ReceiveBufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.ReceiveBufferSize));

        if (options.ListenBacklog <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.ListenBacklog));

        if (options.IdleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.IdleTimeout));
    }

    private readonly record struct ProcessReceivedBytesResult(
        bool ContinueConnection,
        bool CloseAfterSendingResponses,
        IReadOnlyList<byte[]> Responses);
}
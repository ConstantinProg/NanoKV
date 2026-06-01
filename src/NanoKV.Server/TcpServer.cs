using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using NanoKV.Core.Protocol;

namespace NanoKV.Server;

public sealed class TcpServer : IAsyncDisposable
{
    private static readonly TimeSpan ProtocolErrorDrainTimeout =
        TimeSpan.FromMilliseconds(200);

    private readonly IPEndPoint _endpoint;
    private readonly SemaphoreSlim _connectionLimiter;
    private readonly ICommandHandler _commandHandler;
    private readonly NanoKvServerOptions _options;
    private readonly ILogger<TcpServer> _logger;
    private readonly object _syncRoot = new();

    private readonly HashSet<Task> _clientTasks = new();

    private CancellationTokenSource? _serverCts;
    private Socket? _listener;
    private Task? _acceptLoopTask;
    private bool _disposed;

    public TcpServer(
        NanoKvServerOptions options,
        ICommandHandler commandHandler,
        ILogger<TcpServer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(commandHandler);
        ArgumentNullException.ThrowIfNull(logger);

        ValidateOptions(options);

        _options = options;
        _commandHandler = commandHandler;
        _logger = logger;
        _endpoint = new IPEndPoint(IPAddress.Parse(options.Host), options.Port);
        _connectionLimiter = new SemaphoreSlim(options.MaxConcurrentConnections);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            if (_acceptLoopTask is not null)
                throw new InvalidOperationException("TCP server is already started.");

            _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _listener = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp);

            _listener.Bind(_endpoint);
            _listener.Listen(_options.ListenBacklog);

            _acceptLoopTask = AcceptLoopAsync(_serverCts.Token);
        }

        _logger.LogInformation(
            "NanoKV TCP server started on {Host}:{Port}",
            _options.Host,
            _options.Port);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? acceptLoopTask;
        Task[] clientTasks;

        lock (_syncRoot)
        {
            if (_acceptLoopTask is null)
                return;

            _logger.LogInformation("Stopping NanoKV TCP server.");

            _serverCts?.Cancel();

            try
            {
                _listener?.Close();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Listener socket close failed.");
            }

            acceptLoopTask = _acceptLoopTask;
            clientTasks = _clientTasks.ToArray();
        }

        try
        {
            await acceptLoopTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Accept loop finished with an exception during shutdown.");
        }

        if (clientTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(clientTasks).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "TCP server shutdown timeout expired. Active client tasks may still be completing.");

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "One or more client tasks failed during shutdown.");
            }
        }

        lock (_syncRoot)
        {
            _listener?.Dispose();
            _listener = null;

            _serverCts?.Dispose();
            _serverCts = null;

            _acceptLoopTask = null;
        }

        _logger.LogInformation("NanoKV TCP server stopped.");
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        Socket listener = _listener
            ?? throw new InvalidOperationException("Listener socket is not initialized.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Socket client;

                try
                {
                    client = await listener.AcceptAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException ex) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug(ex, "Accept was interrupted by server shutdown.");
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to accept TCP client.");
                    continue;
                }

                if (!_connectionLimiter.Wait(0))
                {
                    Telemetry.RejectedConnections.Add(1);

                    _logger.LogWarning(
                        "Rejected client {RemoteEndPoint}: connection limit exceeded.",
                        client.RemoteEndPoint);

                    _ = TrackClientTaskAsync(
                        RejectClientAsync(client),
                        CancellationToken.None);

                    continue;
                }

                Telemetry.ActiveConnections.Add(1);

                _logger.LogInformation(
                    "Accepted client {RemoteEndPoint}.",
                    client.RemoteEndPoint);

                _ = TrackClientTaskAsync(
                    ProcessClientAndReleaseAsync(client, cancellationToken),
                    cancellationToken);
            }
        }
        finally
        {
            _logger.LogInformation("TCP accept loop completed.");
        }
    }

    private async Task TrackClientTaskAsync(
        Task task,
        CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            _clientTasks.Add(task);
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during graceful shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Client task failed.");
        }
        finally
        {
            lock (_syncRoot)
            {
                _clientTasks.Remove(task);
            }
        }
    }

    private async Task ProcessClientAndReleaseAsync(
        Socket client,
        CancellationToken cancellationToken)
    {
        try
        {
            await ProcessClientAsync(client, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Client {RemoteEndPoint} processing cancelled by server shutdown.",
                SafeRemoteEndPoint(client));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Client {RemoteEndPoint} processing failed.",
                SafeRemoteEndPoint(client));
        }
        finally
        {
            _connectionLimiter.Release();
            Telemetry.ActiveConnections.Add(-1);

            _logger.LogInformation(
                "Client {RemoteEndPoint} disconnected.",
                SafeRemoteEndPoint(client));
        }
    }

    private async Task RejectClientAsync(Socket client)
    {
        try
        {
            byte[] response = ProtocolResponse.Error("too many connections");

            await client.SendAsync(response, CancellationToken.None);
            Telemetry.BytesSent.Add(response.Length);

            await GracefulProtocolCloseAsync(client, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to reject client gracefully.");
            CloseClient(client);
        }
    }

    private async Task ProcessClientAsync(
        Socket client,
        CancellationToken serverCancellationToken)
    {
        byte[] receiveBuffer =
            ArrayPool<byte>.Shared.Rent(_options.ReceiveBufferSize);

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
                    _logger.LogInformation(
                        "Client {RemoteEndPoint} disconnected by idle timeout.",
                        SafeRemoteEndPoint(client));

                    byte[] response = ProtocolResponse.Error("idle timeout");

                    await client.SendAsync(response, CancellationToken.None);
                    Telemetry.BytesSent.Add(response.Length);

                    await GracefulProtocolCloseAsync(
                        client,
                        CancellationToken.None);

                    return;
                }

                if (bytesRead == 0)
                    return;

                Telemetry.BytesReceived.Add(bytesRead);

                ProcessReceivedBytesResult result = ProcessReceivedBytes(
                    receiveBuffer.AsSpan(0, bytesRead),
                    lineBuffer,
                    client.RemoteEndPoint);

                foreach (byte[] response in result.Responses)
                {
                    await client.SendAsync(response, serverCancellationToken);
                    Telemetry.BytesSent.Add(response.Length);
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
        EndPoint? remoteEndPoint)
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
                    _logger.LogWarning(
                        "Closing client {RemoteEndPoint}: command too long.",
                        remoteEndPoint);

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
                _logger.LogWarning(
                    "Closing client {RemoteEndPoint}: command too long.",
                    remoteEndPoint);

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
        EndPoint? remoteEndPoint)
    {
        ParsedCommand command = CommandParser.Parse(line);

        if (command.IsEmpty)
            return ProtocolResponse.Error("empty command");

        string commandName = GetCommandName(command.Type);

        using Activity? activity = Telemetry.ActivitySource.StartActivity(
            "nanokv.command.process",
            ActivityKind.Server);

        activity?.SetTag("command.name", commandName);

        long startTimestamp = Stopwatch.GetTimestamp();

        byte[] response = _commandHandler.Handle(command);

        double elapsedMilliseconds =
            Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        string commandStatus = GetCommandStatus(response);

        var tags = new TagList
    {
        { "command.name", commandName },
        { "command.status", commandStatus }
    };

        Telemetry.CommandsProcessed.Add(1, tags);
        Telemetry.CommandDuration.Record(elapsedMilliseconds, tags);

        activity?.SetTag("command.status", commandStatus);
        activity?.SetTag("command.response.bytes", response.Length);

        if (commandStatus == "error")
            activity?.SetStatus(ActivityStatusCode.Error);

        _logger.LogDebug(
            "Processed command {CommandName} from {RemoteEndPoint} with status {CommandStatus} in {ElapsedMilliseconds} ms.",
            commandName,
            remoteEndPoint,
            commandStatus,
            elapsedMilliseconds);

        return response;
    }

    private static string GetCommandStatus(ReadOnlySpan<byte> response)
    {
        return response.Length > 0 && response[0] == (byte)'-'
            ? "error"
            : "ok";
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

                Telemetry.BytesReceived.Add(read);
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
            // Socket may already be closed.
        }

        client.Dispose();
    }

    private static EndPoint? SafeRemoteEndPoint(Socket client)
    {
        try
        {
            return client.RemoteEndPoint;
        }
        catch
        {
            return null;
        }
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

    private static void ValidateOptions(NanoKvServerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Host);

        if (options.Port <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.Port));

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

        if (options.ShutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.ShutdownTimeout));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        using var cts = new CancellationTokenSource(_options.ShutdownTimeout);

        try
        {
            await StopAsync(cts.Token);
        }
        catch
        {
            // Dispose must be best-effort.
        }

        _connectionLimiter.Dispose();
        _disposed = true;
    }

    private readonly record struct ProcessReceivedBytesResult(
        bool ContinueConnection,
        bool CloseAfterSendingResponses,
        IReadOnlyList<byte[]> Responses);
}
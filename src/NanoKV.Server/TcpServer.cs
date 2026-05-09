using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NanoKV.Core.Protocol;

namespace NanoKV.Server;

public sealed class TcpServer
{
    private const int MaxConcurrentConnections = 100;
    private const int MaxIncomingMessageBytes = 4 * 1024;
    private const int ReceiveBufferSize = 4096;
    private const int ListenBacklog = 100;

    private readonly IPEndPoint _endpoint;
    private readonly SemaphoreSlim _connectionLimiter;
    private readonly ICommandHandler _commandHandler;

    public TcpServer(string ip, int port, ICommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _endpoint = new IPEndPoint(IPAddress.Parse(ip), port);
        _commandHandler = handler;
        _connectionLimiter = new SemaphoreSlim(MaxConcurrentConnections);
    }

    public async Task StartAsync(CancellationToken token)
    {
        using var server = new Socket(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp);

        server.Bind(_endpoint);
        server.Listen(ListenBacklog);

        while (!token.IsCancellationRequested)
        {
            Socket client;

            try
            {
                client = await server.AcceptAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await _connectionLimiter.WaitAsync(token);
            }
            catch
            {
                client.Dispose();
                throw;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessClientAsync(client, token);
                }
                finally
                {
                    _connectionLimiter.Release();
                }
            }, CancellationToken.None);
        }
    }

    private async Task ProcessClientAsync(Socket client, CancellationToken token)
    {
        var pool = ArrayPool<byte>.Shared;
        byte[] buffer = pool.Rent(ReceiveBufferSize);

        var ring = new RingBuffer(MaxIncomingMessageBytes + 1);

        try
        {
            while (!token.IsCancellationRequested)
            {
                int bytesRead = await client.ReceiveAsync(buffer, token);

                if (bytesRead == 0)
                    break;

                var received = buffer.AsSpan(0, bytesRead);
                for (int i = 0; i < bytesRead; i++)
                {
                    byte value = buffer[i];

                    if (ring.Count >= MaxIncomingMessageBytes && value != (byte)'\n')
                    {
                        return;
                    }

                    ring.WriteByte(value);

                    if (value != (byte)'\n')
                        continue;

                    while (ring.TryReadLine(out byte[] line))
                    {
                        if (line.Length > MaxIncomingMessageBytes)
                            return;

                        byte[] response = ProcessCommand(line, client.RemoteEndPoint);
                        await client.SendAsync(response, token);
                    }
                }
            }
        }
        finally
        {
            pool.Return(buffer);

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
    }
    private byte[] ProcessCommand(byte[] line, EndPoint? remoteEndPoint)
    {
        var command = CommandParser.Parse(line);

        if (command.IsEmpty)
            return [];

        string commandName = Encoding.UTF8
            .GetString(command.Command)
            .ToUpperInvariant();

        string? key = command.Key.IsEmpty
            ? null
            : Encoding.UTF8.GetString(command.Key);

        using var activity = Telemetry.ActivitySource.StartActivity(
            "Process command",
            ActivityKind.Server);

        activity?.SetTag("command.name", commandName);
        activity?.SetTag("command.has_key", key is not null);
        activity?.SetTag("command.key", key);
        activity?.SetTag("net.peer", remoteEndPoint?.ToString());

        var stopwatch = Stopwatch.StartNew();

        byte[] response = _commandHandler.Handle(command);

        stopwatch.Stop();

        var tags = new TagList
    {
        { "command.name", commandName },
        { "command.has_key", key is not null }
    };

        Telemetry.CommandsProcessed.Add(1, tags);
        Telemetry.CommandDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

        return response;
    }
}
using NanoKV.Core.Protocol;
using NanoKV.Core.Storage;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NanoKV.Server;

public sealed class TcpServer
{
    private readonly IPEndPoint _endpoint;
    private readonly SemaphoreSlim _connectionLimiter = new(100);
    private readonly ICommandHandler _commandHandler;

    public TcpServer(string ip, int port, ICommandHandler handler)
    {
        _endpoint = new IPEndPoint(IPAddress.Parse(ip), port);
        _commandHandler = handler;
    }
    public async Task StartAsync(CancellationToken token)
    {
        using var server = new Socket(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp);

        server.Bind(_endpoint);
        server.Listen(100);
        while (!token.IsCancellationRequested)
        {
            var client = await server.AcceptAsync(token);

            await _connectionLimiter.WaitAsync(token);

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
            }, token);
        }
    }
    private async Task ProcessClientAsync(Socket client, CancellationToken token)
    {
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(4096);

        var ring = new RingBuffer(8192);

        try
        {
            while (!token.IsCancellationRequested)
            {
                int bytesRead = await client.ReceiveAsync(buffer, token);

                if (bytesRead == 0)
                    break;

                ring.Write(buffer.AsSpan(0, bytesRead));

                while (ring.TryReadLine(out var line))
                {
                    var cmd = CommandParser.Parse(line);
                    if (!cmd.IsEmpty)
                    {
                        var response = await _commandHandler.HandleAsync(cmd);
                        await client.SendAsync(response, token);
                    }
                }
            }
        }
        finally
        {
            pool.Return(buffer);

            try { client.Shutdown(SocketShutdown.Both); }
            catch { }

            client.Dispose();
        }
    }
}

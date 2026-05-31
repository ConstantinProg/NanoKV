using Microsoft.Extensions.Logging.Abstractions;
using NanoKV.Core.Storage;
using NanoKV.Server;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NanoKV.Server.Tests;

public sealed class TcpServerTests
{
    [Fact]
    public async Task StartAsync_AllowsClientToConnect()
    {
        await using TestServer server = await TestServer.StartAsync();

        using var client = new TcpClient();

        await client.ConnectAsync(IPAddress.Loopback, server.Port);

        Assert.True(client.Connected);
    }

    [Fact]
    public async Task Server_ProcessesSetCommand()
    {
        await using TestServer server = await TestServer.StartAsync();

        using var client = new TcpClient();

        await client.ConnectAsync(IPAddress.Loopback, server.Port);

        NetworkStream stream = client.GetStream();

        await WriteAsync(stream, "SET user:1 data\n");

        string response = await ReadLineAsync(stream);

        Assert.Equal("+OK\r\n", response);
    }

    [Fact]
    public async Task Server_ProcessesSetAndGetCommands()
    {
        await using TestServer server = await TestServer.StartAsync();

        using var client = new TcpClient();

        await client.ConnectAsync(IPAddress.Loopback, server.Port);

        NetworkStream stream = client.GetStream();

        await WriteAsync(stream, "SET user:1 data\n");
        string setResponse = await ReadLineAsync(stream);

        await WriteAsync(stream, "GET user:1\n");
        string getResponse = await ReadBulkStringAsync(stream);

        Assert.Equal("+OK\r\n", setResponse);
        Assert.Equal("$4\r\ndata\r\n", getResponse);
    }

    [Fact]
    public async Task StopAsync_StopsAcceptingNewConnections()
    {
        await using TestServer server = await TestServer.StartAsync();

        await server.StopAsync();

        using var client = new TcpClient();

        await Assert.ThrowsAnyAsync<SocketException>(async () =>
        {
            await client.ConnectAsync(IPAddress.Loopback, server.Port);
        });
    }

    [Fact]
    public async Task Server_ContinuesWorking_AfterInvalidCommand()
    {
        await using TestServer server = await TestServer.StartAsync();

        using var client = new TcpClient();

        await client.ConnectAsync(IPAddress.Loopback, server.Port);

        NetworkStream stream = client.GetStream();

        await WriteAsync(stream, "UNKNOWN\n");
        string errorResponse = await ReadLineAsync(stream);

        await WriteAsync(stream, "SET key value\n");
        string setResponse = await ReadLineAsync(stream);

        Assert.Equal("-ERR unknown command\r\n", errorResponse);
        Assert.Equal("+OK\r\n", setResponse);
    }

    [Fact]
    public async Task Server_CanStartAndStopTwice_OnDifferentPorts()
    {
        await using TestServer firstServer = await TestServer.StartAsync();

        await firstServer.StopAsync();

        await using TestServer secondServer = await TestServer.StartAsync();

        using var client = new TcpClient();

        await client.ConnectAsync(IPAddress.Loopback, secondServer.Port);

        Assert.True(client.Connected);
    }

    private static async Task WriteAsync(NetworkStream stream, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);

        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var output = new MemoryStream();

        var buffer = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(buffer, cts.Token);

            if (read == 0)
                break;

            output.WriteByte(buffer[0]);

            if (buffer[0] == (byte)'\n')
                break;
        }

        return Encoding.ASCII.GetString(output.ToArray());
    }

    private static async Task<string> ReadBulkStringAsync(NetworkStream stream)
    {
        string firstLine = await ReadLineAsync(stream);

        if (!firstLine.StartsWith('$'))
            return firstLine;

        int length = int.Parse(firstLine.AsSpan(1, firstLine.Length - 3));

        byte[] payloadAndCrLf = await ReadExactAsync(stream, length + 2);

        return firstLine + Encoding.ASCII.GetString(payloadAndCrLf);
    }

    private static async Task<byte[]> ReadExactAsync(
        NetworkStream stream,
        int length)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        byte[] buffer = new byte[length];
        int offset = 0;

        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cts.Token);

            if (read == 0)
                throw new IOException("Unexpected end of stream.");

            offset += read;
        }

        return buffer;
    }

    private sealed class TestServer : IAsyncDisposable
    {
        private readonly SimpleStore _store;
        private readonly TcpServer _server;
        private bool _stopped;

        private TestServer(
            int port,
            SimpleStore store,
            TcpServer server)
        {
            Port = port;
            _store = store;
            _server = server;
        }

        public int Port { get; }

        public static async Task<TestServer> StartAsync()
        {
            int port = GetFreeTcpPort();

            var options = new NanoKvServerOptions
            {
                Host = "127.0.0.1",
                Port = port,
                MaxConcurrentConnections = 100,
                MaxCommandBytes = 4 * 1024,
                ReceiveBufferSize = 512,
                ListenBacklog = 100,
                IdleTimeout = TimeSpan.FromSeconds(5),
                ShutdownTimeout = TimeSpan.FromSeconds(5)
            };

            var store = new SimpleStore();
            var handler = new StoreCommandHandler(store);

            var server = new TcpServer(
                options,
                handler,
                NullLogger<TcpServer>.Instance);

            await server.StartAsync();

            await WaitUntilAcceptingConnectionsAsync(port);

            return new TestServer(port, store, server);
        }

        public async Task StopAsync()
        {
            if (_stopped)
                return;

            _stopped = true;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await _server.StopAsync(cts.Token);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopAsync();
            }
            finally
            {
                await _server.DisposeAsync();
                _store.Dispose();
            }
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);

            listener.Start();

            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            listener.Stop();

            return port;
        }

        private static async Task WaitUntilAcceptingConnectionsAsync(int port)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var client = new TcpClient();

                    await client.ConnectAsync(
                        IPAddress.Loopback,
                        port,
                        cts.Token);

                    return;
                }
                catch
                {
                    await Task.Delay(25, cts.Token);
                }
            }

            throw new TimeoutException("TCP server did not start.");
        }
    }
}
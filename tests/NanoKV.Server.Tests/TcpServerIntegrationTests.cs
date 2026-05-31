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

public sealed class TcpServerIntegrationTests
{
    [Fact]
    public async Task Server_HandlesCommandSplitAcrossReceives()
    {
        using TestServer server = await TestServer.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);

        NetworkStream stream = client.GetStream();

        await WriteAsync(stream, "SET key ");
        await WriteAsync(stream, "value\n");

        string setResponse = await ReadSimpleResponseAsync(stream);

        await WriteAsync(stream, "GET key\n");

        string getResponse = await ReadBulkOrSimpleResponseAsync(stream);

        Assert.Equal("+OK\r\n", setResponse);
        Assert.Equal("$5\r\nvalue\r\n", getResponse);
    }

    [Fact]
    public async Task Server_HandlesMultipleCommandsInSingleReceive()
    {
        using TestServer server = await TestServer.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);

        NetworkStream stream = client.GetStream();

        await WriteAsync(stream, "SET key value\nGET key\nDELETE key\nGET key\n");

        string setResponse = await ReadSimpleResponseAsync(stream);
        string getExistingResponse = await ReadBulkOrSimpleResponseAsync(stream);
        string deleteResponse = await ReadSimpleResponseAsync(stream);
        string getMissingResponse = await ReadBulkOrSimpleResponseAsync(stream);

        Assert.Equal("+OK\r\n", setResponse);
        Assert.Equal("$5\r\nvalue\r\n", getExistingResponse);
        Assert.Equal("+OK\r\n", deleteResponse);
        Assert.Equal("$-1\r\n", getMissingResponse);
    }

    [Fact]
    public async Task Server_KeepsCommandWithoutNewlineUntilNewlineArrives()
    {
        using TestServer server = await TestServer.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);

        NetworkStream stream = client.GetStream();

        await WriteAsync(stream, "SET key value");

        await Task.Delay(100);

        Assert.False(stream.DataAvailable);

        await WriteAsync(stream, "\nGET key\n");

        string setResponse = await ReadSimpleResponseAsync(stream);
        string getResponse = await ReadBulkOrSimpleResponseAsync(stream);

        Assert.Equal("+OK\r\n", setResponse);
        Assert.Equal("$5\r\nvalue\r\n", getResponse);
    }

    [Fact]
    public async Task Server_ReturnsErrorAndClosesConnection_WhenCommandIsTooLong()
    {
        var options = new NanoKvServerOptions
        {
            MaxCommandBytes = 8,
            ReceiveBufferSize = 4,
            MaxConcurrentConnections = 100,
            ListenBacklog = 100,
            IdleTimeout = TimeSpan.FromSeconds(5)
        };

        using TestServer server = await TestServer.StartAsync(options);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);

        NetworkStream stream = client.GetStream();

        await WriteAsync(stream, "SET key value\n");

        string response = await ReadSimpleResponseAsync(stream);

        Assert.Equal("-ERR command too long\r\n", response);
    }

    [Fact]
    public async Task Server_ReturnsIdleTimeoutAndClosesConnection_WhenClientIsIdle()
    {
        var options = new NanoKvServerOptions
        {
            MaxCommandBytes = 1024,
            ReceiveBufferSize = 128,
            MaxConcurrentConnections = 100,
            ListenBacklog = 100,
            IdleTimeout = TimeSpan.FromMilliseconds(150)
        };

        using TestServer server = await TestServer.StartAsync(options);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);

        NetworkStream stream = client.GetStream();

        string response = await ReadSimpleResponseAsync(stream);

        Assert.Equal("-ERR idle timeout\r\n", response);
    }

    [Fact]
    public async Task Server_RejectsConnection_WhenConnectionLimitIsExceeded()
    {
        var options = new NanoKvServerOptions
        {
            MaxConcurrentConnections = 1,
            MaxCommandBytes = 1024,
            ReceiveBufferSize = 128,
            ListenBacklog = 100,
            IdleTimeout = TimeSpan.FromSeconds(5)
        };

        using TestServer server = await TestServer.StartAsync(options);

        using var firstClient = new TcpClient();
        await firstClient.ConnectAsync(IPAddress.Loopback, server.Port);

        NetworkStream firstStream = firstClient.GetStream();

        await WriteAsync(firstStream, "STATS\n");

        string firstResponse = await ReadBulkOrSimpleResponseAsync(firstStream);

        Assert.StartsWith("$", firstResponse, StringComparison.Ordinal);

        using var secondClient = new TcpClient();
        await secondClient.ConnectAsync(IPAddress.Loopback, server.Port);

        NetworkStream secondStream = secondClient.GetStream();

        string secondResponse = await ReadSimpleResponseAsync(secondStream);

        Assert.Equal("-ERR too many connections\r\n", secondResponse);
    }

    [Fact]
    public async Task Server_ContinuesWorking_AfterClientDisconnects()
    {
        using TestServer server = await TestServer.StartAsync();

        using (var firstClient = new TcpClient())
        {
            await firstClient.ConnectAsync(IPAddress.Loopback, server.Port);

            NetworkStream firstStream = firstClient.GetStream();

            await WriteAsync(firstStream, "SET key value\n");

            string response = await ReadSimpleResponseAsync(firstStream);

            Assert.Equal("+OK\r\n", response);
        }

        using var secondClient = new TcpClient();
        await secondClient.ConnectAsync(IPAddress.Loopback, server.Port);

        NetworkStream secondStream = secondClient.GetStream();

        await WriteAsync(secondStream, "GET key\n");

        string getResponse = await ReadBulkOrSimpleResponseAsync(secondStream);

        Assert.Equal("$5\r\nvalue\r\n", getResponse);
    }

    private static async Task WriteAsync(NetworkStream stream, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static async Task<string> ReadSimpleResponseAsync(NetworkStream stream)
    {
        return await ReadLineAsync(stream);
    }

    private static async Task<string> ReadBulkOrSimpleResponseAsync(NetworkStream stream)
    {
        string firstLine = await ReadLineAsync(stream);

        if (!firstLine.StartsWith('$'))
            return firstLine;

        if (firstLine == "$-1\r\n")
            return firstLine;

        int length = int.Parse(firstLine.AsSpan(1, firstLine.Length - 3));

        byte[] payloadAndCrLf = await ReadExactAsync(stream, length + 2);

        return firstLine + Encoding.ASCII.GetString(payloadAndCrLf);
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

    private sealed class TestServer : IDisposable
    {
        private readonly SimpleStore _store;
        private readonly TcpServer _server;

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

        public static async Task<TestServer> StartAsync(
            NanoKvServerOptions? options = null)
        {
            int port = GetFreeTcpPort();

            NanoKvServerOptions serverOptions = options is null
                ? CreateDefaultOptions(port)
                : CreateOptionsForPort(options, port);

            var store = new SimpleStore();
            var handler = new StoreCommandHandler(store);

            var server = new TcpServer(
                serverOptions,
                handler,
                NullLogger<TcpServer>.Instance);

            await server.StartAsync();

            await WaitUntilAcceptingConnectionsAsync(port);

            return new TestServer(port, store, server);
        }

        public void Dispose()
        {
            try
            {
                _server
                    .StopAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            finally
            {
                _server
                    .DisposeAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();

                _store.Dispose();
            }
        }

        private static NanoKvServerOptions CreateDefaultOptions(int port)
        {
            return new NanoKvServerOptions
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
        }

        private static NanoKvServerOptions CreateOptionsForPort(
            NanoKvServerOptions source,
            int port)
        {
            return new NanoKvServerOptions
            {
                Host = "127.0.0.1",
                Port = port,
                MaxConcurrentConnections = source.MaxConcurrentConnections,
                MaxCommandBytes = source.MaxCommandBytes,
                ReceiveBufferSize = source.ReceiveBufferSize,
                ListenBacklog = source.ListenBacklog,
                IdleTimeout = source.IdleTimeout,
                ShutdownTimeout = source.ShutdownTimeout
            };
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
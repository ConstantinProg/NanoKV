using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NanoKV.Core.Models;

namespace NanoKV.LoadTests;

public sealed class NanoKvClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;

    private TcpClient? _client;
    private NetworkStream? _stream;

    public NanoKvClient(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(_host, _port, cancellationToken);

        _stream = _client.GetStream();
    }

    public async Task SetAsync(
        string key,
        UserProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(profile);

        string json = JsonSerializer.Serialize(profile);
        byte[] command = Encoding.UTF8.GetBytes($"SET {key} {json}\n");

        await SendAsync(command, cancellationToken);

        string response = await ReadLineAsync(cancellationToken);

        if (response != "OK")
            throw new InvalidOperationException($"Unexpected SET response: {response}");
    }

    public async Task<string> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        byte[] command = Encoding.UTF8.GetBytes($"GET {key}\n");

        await SendAsync(command, cancellationToken);

        return await ReadLineAsync(cancellationToken);
    }

    private async Task SendAsync(
        byte[] command,
        CancellationToken cancellationToken)
    {
        if (_stream is null)
            throw new InvalidOperationException("Client is not connected.");

        await _stream.WriteAsync(command, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
            throw new InvalidOperationException("Client is not connected.");

        var buffer = new List<byte>();
        var temp = new byte[1];

        while (true)
        {
            int read = await _stream.ReadAsync(temp, cancellationToken);

            if (read == 0)
                throw new IOException("Connection closed by server.");

            if (temp[0] == (byte)'\n')
                break;

            if (temp[0] != (byte)'\r')
                buffer.Add(temp[0]);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        _stream?.Dispose();

        if (_client is not null)
        {
            await Task.Yield();
            _client.Dispose();
        }
    }
}
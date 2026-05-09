using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NanoKV.Core.Models;

namespace NanoKV.LoadTests;

public sealed class NanoKvClient : IAsyncDisposable
{
    private const int ReceiveBufferSize = 8192;

    private readonly string _host;
    private readonly int _port;
    private readonly byte[] _receiveBuffer = new byte[ReceiveBufferSize];

    private TcpClient? _client;
    private NetworkStream? _stream;

    private int _receiveOffset;
    private int _receiveCount;
    private bool _disposed;

    public NanoKvClient(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (port <= 0)
            throw new ArgumentOutOfRangeException(nameof(port));

        _host = host;
        _port = port;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_client is not null)
            throw new InvalidOperationException("Client is already connected.");

        _client = new TcpClient();

        try
        {
            await _client.ConnectAsync(_host, _port, cancellationToken)
                .ConfigureAwait(false);

            _stream = _client.GetStream();
        }
        catch
        {
            _client.Dispose();
            _client = null;
            _stream = null;

            throw;
        }
    }

    public async Task SetAsync(
        string key,
        UserProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(profile);

        ThrowIfDisposed();

        string json = JsonSerializer.Serialize(profile);
        byte[] command = Encoding.UTF8.GetBytes($"SET {key} {json}\n");

        await SendAsync(command, cancellationToken).ConfigureAwait(false);

        string response = await ReadLineAsync(cancellationToken).ConfigureAwait(false);

        if (response != "OK")
            throw new InvalidOperationException($"Unexpected SET response: {response}");
    }

    public async Task<string> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        ThrowIfDisposed();

        byte[] command = Encoding.UTF8.GetBytes($"GET {key}\n");

        await SendAsync(command, cancellationToken).ConfigureAwait(false);

        return await ReadLineAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SendAsync(
        byte[] command,
        CancellationToken cancellationToken)
    {
        NetworkStream stream = GetStream();

        await stream.WriteAsync(command, cancellationToken)
            .ConfigureAwait(false);

        await stream.FlushAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        NetworkStream stream = GetStream();

        var line = new ArrayBufferWriter<byte>();

        while (true)
        {
            if (_receiveOffset >= _receiveCount)
            {
                _receiveCount = await stream.ReadAsync(
                        _receiveBuffer.AsMemory(0, _receiveBuffer.Length),
                        cancellationToken)
                    .ConfigureAwait(false);

                _receiveOffset = 0;

                if (_receiveCount == 0)
                    throw new IOException("Connection closed by server.");
            }

            int lineEnd = FindLineFeed(
                _receiveBuffer,
                _receiveOffset,
                _receiveCount);

            if (lineEnd >= 0)
            {
                int length = lineEnd - _receiveOffset;

                if (length > 0)
                {
                    line.Write(_receiveBuffer.AsSpan(_receiveOffset, length));
                }

                _receiveOffset = lineEnd + 1;

                ReadOnlySpan<byte> result = line.WrittenSpan;

                if (result.Length > 0 && result[^1] == (byte)'\r')
                    result = result[..^1];

                return Encoding.UTF8.GetString(result);
            }

            int remaining = _receiveCount - _receiveOffset;

            if (remaining > 0)
            {
                line.Write(_receiveBuffer.AsSpan(_receiveOffset, remaining));
                _receiveOffset = _receiveCount;
            }
        }
    }

    private static int FindLineFeed(byte[] buffer, int offset, int count)
    {
        for (int i = offset; i < count; i++)
        {
            if (buffer[i] == (byte)'\n')
                return i;
        }

        return -1;
    }

    private NetworkStream GetStream()
    {
        ThrowIfDisposed();

        return _stream
            ?? throw new InvalidOperationException("Client is not connected.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NanoKvClient));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }

        _client?.Dispose();
        _client = null;

        _receiveOffset = 0;
        _receiveCount = 0;
    }
}
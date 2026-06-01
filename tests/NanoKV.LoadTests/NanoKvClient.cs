using System.Buffers;
using System.Net.Sockets;
using System.Text;

namespace NanoKV.LoadTests;

public sealed class NanoKvClient : IAsyncDisposable
{
    private static readonly byte[] NewLine = "\n"u8.ToArray();

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

        _client = new TcpClient
        {
            NoDelay = true
        };

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
        ReadOnlyMemory<byte> value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ThrowIfDisposed();

        NetworkStream stream = GetStream();

        byte[] prefix = Encoding.UTF8.GetBytes($"SET {key} ");

        await stream.WriteAsync(prefix, cancellationToken)
            .ConfigureAwait(false);

        await stream.WriteAsync(value, cancellationToken)
            .ConfigureAwait(false);

        await stream.WriteAsync(NewLine, cancellationToken)
            .ConfigureAwait(false);

        string response = await ReadLineAsync(cancellationToken)
            .ConfigureAwait(false);

        if (response != "+OK\r\n")
            throw new InvalidOperationException($"Unexpected SET response: {response}");
    }

    public async Task<string?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ThrowIfDisposed();

        await SendCommandAsync($"GET {key}\n", cancellationToken)
            .ConfigureAwait(false);

        string firstLine = await ReadLineAsync(cancellationToken)
            .ConfigureAwait(false);

        if (firstLine == "$-1\r\n")
            return null;

        if (!firstLine.StartsWith('$'))
            throw new InvalidOperationException($"Unexpected GET response: {firstLine}");

        int length = ParseBulkLength(firstLine);

        byte[] payloadAndCrLf = await ReadExactAsync(
                length + 2,
                cancellationToken)
            .ConfigureAwait(false);

        if (payloadAndCrLf[^2] != (byte)'\r' ||
            payloadAndCrLf[^1] != (byte)'\n')
        {
            throw new InvalidOperationException("Invalid bulk string terminator.");
        }

        return Encoding.UTF8.GetString(payloadAndCrLf, 0, length);
    }

    public async Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ThrowIfDisposed();

        await SendCommandAsync($"DELETE {key}\n", cancellationToken)
            .ConfigureAwait(false);

        string response = await ReadLineAsync(cancellationToken)
            .ConfigureAwait(false);

        if (response != "+OK\r\n")
            throw new InvalidOperationException($"Unexpected DELETE response: {response}");
    }

    private async Task SendCommandAsync(
        string command,
        CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(command);

        NetworkStream stream = GetStream();

        await stream.WriteAsync(bytes, cancellationToken)
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
                int length = lineEnd - _receiveOffset + 1;

                line.Write(_receiveBuffer.AsSpan(_receiveOffset, length));

                _receiveOffset = lineEnd + 1;

                return Encoding.UTF8.GetString(line.WrittenSpan);
            }

            int remaining = _receiveCount - _receiveOffset;

            if (remaining > 0)
            {
                line.Write(_receiveBuffer.AsSpan(_receiveOffset, remaining));
                _receiveOffset = _receiveCount;
            }
        }
    }

    private async Task<byte[]> ReadExactAsync(
        int length,
        CancellationToken cancellationToken)
    {
        byte[] result = new byte[length];
        int written = 0;

        if (_receiveOffset < _receiveCount)
        {
            int buffered = Math.Min(
                length,
                _receiveCount - _receiveOffset);

            Buffer.BlockCopy(
                _receiveBuffer,
                _receiveOffset,
                result,
                written,
                buffered);

            _receiveOffset += buffered;
            written += buffered;
        }

        NetworkStream stream = GetStream();

        while (written < length)
        {
            int read = await stream.ReadAsync(
                    result.AsMemory(written, length - written),
                    cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
                throw new IOException("Connection closed by server.");

            written += read;
        }

        return result;
    }

    private static int ParseBulkLength(string line)
    {
        ReadOnlySpan<char> lengthSpan = line.AsSpan(1, line.Length - 3);

        if (!int.TryParse(lengthSpan, out int length) || length < 0)
            throw new InvalidOperationException($"Invalid bulk string length: {line}");

        return length;
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
        ObjectDisposedException.ThrowIf(_disposed, this);
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
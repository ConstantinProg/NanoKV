using System.Buffers.Text;
using System.Text;

namespace NanoKV.Server;

internal static class ProtocolResponse
{
    private static readonly byte[] OkResponse = "+OK\r\n"u8.ToArray();
    private static readonly byte[] NullBulkStringResponse = "$-1\r\n"u8.ToArray();

    public static byte[] Ok()
    {
        return OkResponse;
    }

    public static byte[] NullBulkString()
    {
        return NullBulkStringResponse;
    }

    public static byte[] Error(string message)
    {
        return Encoding.ASCII.GetBytes($"-ERR {message}\r\n");
    }

    public static byte[] BulkString(ReadOnlySpan<byte> value)
    {
        Span<byte> lengthBuffer = stackalloc byte[20];

        if (!Utf8Formatter.TryFormat(value.Length, lengthBuffer, out int lengthBytesWritten))
            throw new InvalidOperationException("Failed to format bulk string length.");

        ReadOnlySpan<byte> length = lengthBuffer[..lengthBytesWritten];

        byte[] response = new byte[
            1 +
            length.Length +
            2 +
            value.Length +
            2];

        int offset = 0;

        response[offset++] = (byte)'$';

        length.CopyTo(response.AsSpan(offset));
        offset += length.Length;

        response[offset++] = (byte)'\r';
        response[offset++] = (byte)'\n';

        value.CopyTo(response.AsSpan(offset));
        offset += value.Length;

        response[offset++] = (byte)'\r';
        response[offset] = (byte)'\n';

        return response;
    }
}
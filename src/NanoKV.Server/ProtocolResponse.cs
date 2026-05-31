using System.Text;

namespace NanoKV.Server;

internal static class ProtocolResponse
{
    public static byte[] Ok()
    {
        return "+OK\r\n"u8.ToArray();
    }

    public static byte[] NullBulkString()
    {
        return "$-1\r\n"u8.ToArray();
    }

    public static byte[] Error(string message)
    {
        return Encoding.UTF8.GetBytes($"-ERR {message}\r\n");
    }

    public static byte[] BulkString(ReadOnlySpan<byte> value)
    {
        byte[] length = Encoding.ASCII.GetBytes(value.Length.ToString());

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
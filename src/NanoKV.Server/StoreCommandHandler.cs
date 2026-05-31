using System.Text;
using NanoKV.Core.Protocol;
using NanoKV.Core.Storage;

namespace NanoKV.Server;

public sealed class StoreCommandHandler : ICommandHandler
{
    private readonly SimpleStore _store;

    public StoreCommandHandler(SimpleStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    public byte[] Handle(ParsedCommand cmd)
    {
        string command = Encoding.UTF8.GetString(cmd.Command).ToUpperInvariant();

        return command switch
        {
            "SET" => HandleSet(cmd),
            "GET" => HandleGet(cmd),
            "DELETE" => HandleDelete(cmd),
            _ => Encode("-ERR Unknown command\r\n")
        };
    }

    private byte[] HandleSet(ParsedCommand cmd)
    {
        if (cmd.Key.IsEmpty || cmd.Value.IsEmpty)
            return Encode("-ERR wrong number of arguments\r\n");

        string key = Encoding.UTF8.GetString(cmd.Key);

        try
        {
            _store.Set(key, cmd.Value);
            return Encode("OK\r\n");
        }
        catch (ArgumentException)
        {
            return Encode("-ERR invalid key\r\n");
        }
    }

    private byte[] HandleGet(ParsedCommand cmd)
    {
        if (cmd.Key.IsEmpty)
            return Encode("-ERR wrong number of arguments\r\n");

        string key = Encoding.UTF8.GetString(cmd.Key);

        try
        {
            if (!_store.TryGet(key, out byte[]? value))
                return Encode("(nil)\r\n");

            return EncodeBulkString(value);
        }
        catch (ArgumentException)
        {
            return Encode("-ERR invalid key\r\n");
        }
    }

    private byte[] HandleDelete(ParsedCommand cmd)
    {
        if (cmd.Key.IsEmpty)
            return Encode("-ERR wrong number of arguments\r\n");

        string key = Encoding.UTF8.GetString(cmd.Key);

        try
        {
            _store.Delete(key);
            return Encode("OK\r\n");
        }
        catch (ArgumentException)
        {
            return Encode("-ERR invalid key\r\n");
        }
    }

    private static byte[] Encode(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    private static byte[] EncodeBulkString(byte[] value)
    {
        byte[] prefix = Encoding.UTF8.GetBytes(value.Length.ToString());
        byte[] result = new byte[prefix.Length + 2 + value.Length + 2];

        prefix.CopyTo(result, 0);
        result[prefix.Length] = (byte)'\r';
        result[prefix.Length + 1] = (byte)'\n';

        value.CopyTo(result, prefix.Length + 2);

        result[^2] = (byte)'\r';
        result[^1] = (byte)'\n';

        return result;
    }
}
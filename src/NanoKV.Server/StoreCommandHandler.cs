using NanoKV.Core.Protocol;
using NanoKV.Core.Storage;
using System.Text;

namespace NanoKV.Server;

public sealed class StoreCommandHandler : ICommandHandler
{
    private readonly SimpleStore _store;

    public StoreCommandHandler(SimpleStore store)
    {
        _store = store;
    }
    public ValueTask<byte[]> HandleAsync(ParsedCommand cmd)
    {
        var command = Encoding.ASCII.GetString(cmd.Command).ToUpperInvariant();

        switch (command)
        {
            case "SET":
                {
                    if (cmd.Key.IsEmpty || cmd.Value.IsEmpty)
                        return ValueTask.FromResult(Encoding.UTF8.GetBytes("-ERR wrong number of arguments\r\n"));

                    var key = Encoding.ASCII.GetString(cmd.Key);
                    var value = cmd.Value.ToArray();

                    _store.Set(key, value);
                    return ValueTask.FromResult(Encoding.UTF8.GetBytes("OK\r\n"));
                }

            case "GET":
                {
                    if (cmd.Key.IsEmpty)
                        return ValueTask.FromResult(Encoding.UTF8.GetBytes("-ERR wrong number of arguments\r\n"));

                    var key = Encoding.ASCII.GetString(cmd.Key);
                    var result = _store.Get(key);

                    if (result is null)
                        return ValueTask.FromResult(Encoding.UTF8.GetBytes("(nil)\r\n"));

                    var response = new byte[result.Length + 2];
                    Buffer.BlockCopy(result, 0, response, 0, result.Length);
                    response[^2] = (byte)'\r';
                    response[^1] = (byte)'\n';

                    return ValueTask.FromResult(response);
                }

            case "DELETE":
                {
                    if (cmd.Key.IsEmpty)
                        return ValueTask.FromResult(Encoding.UTF8.GetBytes("-ERR wrong number of arguments\r\n"));

                    var key = Encoding.ASCII.GetString(cmd.Key);
                    _store.Delete(key);

                    return ValueTask.FromResult(Encoding.UTF8.GetBytes("OK\r\n"));
                }

            default:
                return ValueTask.FromResult(Encoding.UTF8.GetBytes("-ERR Unknown command\r\n"));
        }
    }

    private static byte[] Encode(string text)
        => Encoding.UTF8.GetBytes(text);

    private static byte[] Concat(byte[] data, string suffix)
    {
        var suffixBytes = Encoding.UTF8.GetBytes(suffix);
        var result = new byte[data.Length + suffixBytes.Length];

        Buffer.BlockCopy(data, 0, result, 0, data.Length);
        Buffer.BlockCopy(suffixBytes, 0, result, data.Length, suffixBytes.Length);

        return result;
    }
}
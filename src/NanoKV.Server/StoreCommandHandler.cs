using System.Text;
using System.Text.Json;
using NanoKV.Core.Models;
using NanoKV.Core.Protocol;
using NanoKV.Core.Storage;

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
        var command = Encoding.UTF8.GetString(cmd.Command).ToUpperInvariant();

        return command switch
        {
            "SET" => HandleSet(cmd),
            "GET" => HandleGet(cmd),
            "DELETE" => HandleDelete(cmd),
            _ => ValueTask.FromResult(Encode("-ERR Unknown command\r\n"))
        };
    }

    private ValueTask<byte[]> HandleSet(ParsedCommand cmd)
    {
        if (cmd.Key.IsEmpty || cmd.Value.IsEmpty)
            return ValueTask.FromResult(Encode("-ERR wrong number of arguments\r\n"));

        var key = Encoding.UTF8.GetString(cmd.Key);

        try
        {
            var profile = JsonSerializer.Deserialize<UserProfile>(cmd.Value);

            if (profile is null)
                return ValueTask.FromResult(Encode("-ERR invalid json\r\n"));

            _store.Set(key, profile);

            return ValueTask.FromResult(Encode("OK\r\n"));
        }
        catch (JsonException)
        {
            return ValueTask.FromResult(Encode("-ERR invalid json\r\n"));
        }
    }

    private ValueTask<byte[]> HandleGet(ParsedCommand cmd)
    {
        if (cmd.Key.IsEmpty)
            return ValueTask.FromResult(Encode("-ERR wrong number of arguments\r\n"));

        var key = Encoding.UTF8.GetString(cmd.Key);
        var profile = _store.Get(key);

        if (profile is null)
            return ValueTask.FromResult(Encode("(nil)\r\n"));

        var json = JsonSerializer.Serialize(profile);

        return ValueTask.FromResult(Encode($"{json}\r\n"));
    }

    private ValueTask<byte[]> HandleDelete(ParsedCommand cmd)
    {
        if (cmd.Key.IsEmpty)
            return ValueTask.FromResult(Encode("-ERR wrong number of arguments\r\n"));

        var key = Encoding.UTF8.GetString(cmd.Key);

        _store.Delete(key);

        return ValueTask.FromResult(Encode("OK\r\n"));
    }

    private static byte[] Encode(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }
}
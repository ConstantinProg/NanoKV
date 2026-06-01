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

    public byte[] Handle(ParsedCommand command)
    {
        if (command.IsEmpty)
            return ProtocolResponse.Error("empty command");

        return command.Type switch
        {
            CommandType.Set => HandleSet(command),
            CommandType.Get => HandleGet(command),
            CommandType.Delete => HandleDelete(command),
            CommandType.Stats => HandleStats(command),
            CommandType.Unknown => ProtocolResponse.Error("unknown command"),
            _ => ProtocolResponse.Error("unknown command")
        };
    }

    private byte[] HandleSet(ParsedCommand command)
    {
        if (command.Key.IsEmpty || command.Value.IsEmpty)
            return ProtocolResponse.Error("SET requires key and non-empty value");

        string key = Encoding.UTF8.GetString(command.Key);

        try
        {
            _store.Set(key, command.Value);
            return ProtocolResponse.Ok();
        }
        catch (ArgumentException)
        {
            return ProtocolResponse.Error("invalid key");
        }
    }

    private byte[] HandleGet(ParsedCommand command)
    {
        if (command.Key.IsEmpty || !command.Value.IsEmpty)
            return ProtocolResponse.Error("GET requires exactly one key");

        string key = Encoding.UTF8.GetString(command.Key);

        try
        {
            return _store.TryGet(key, out byte[]? value)
                ? ProtocolResponse.BulkString(value)
                : ProtocolResponse.NullBulkString();
        }
        catch (ArgumentException)
        {
            return ProtocolResponse.Error("invalid key");
        }
    }

    private byte[] HandleDelete(ParsedCommand command)
    {
        if (command.Key.IsEmpty || !command.Value.IsEmpty)
            return ProtocolResponse.Error("DELETE requires exactly one key");

        string key = Encoding.UTF8.GetString(command.Key);

        try
        {
            _store.Delete(key);
            return ProtocolResponse.Ok();
        }
        catch (ArgumentException)
        {
            return ProtocolResponse.Error("invalid key");
        }
    }

    private byte[] HandleStats(ParsedCommand command)
    {
        if (!command.Key.IsEmpty || !command.Value.IsEmpty)
            return ProtocolResponse.Error("STATS does not accept arguments");

        StoreStatistics statistics = _store.GetStatistics();

        string payload =
            $"sets={statistics.SetCount};gets={statistics.GetCount};deletes={statistics.DeleteCount};items={statistics.ItemCount}";

        return ProtocolResponse.BulkString(Encoding.ASCII.GetBytes(payload));
    }
}
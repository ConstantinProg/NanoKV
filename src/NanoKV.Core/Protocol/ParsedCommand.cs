namespace NanoKV.Core.Protocol;

public readonly ref struct ParsedCommand
{
    public ParsedCommand(
        CommandType type,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> value,
        bool hasCommand)
    {
        Type = type;
        Key = key;
        Value = value;
        HasCommand = hasCommand;
    }

    public CommandType Type { get; }

    public ReadOnlySpan<byte> Key { get; }

    public ReadOnlySpan<byte> Value { get; }

    public bool HasCommand { get; }

    public bool IsEmpty => !HasCommand;
}
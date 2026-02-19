namespace NanoKv.Core.Protocol;

public readonly ref struct ParsedCommand
{
    public ReadOnlySpan<byte> Command { get; }
    public ReadOnlySpan<byte> Key { get; }
    public ReadOnlySpan<byte> Value { get; }

    public ParsedCommand(
        ReadOnlySpan<byte> command,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> value)
    {
        Command = command;
        Key = key;
        Value = value;
    }

    public bool IsEmpty => Command.IsEmpty;
}

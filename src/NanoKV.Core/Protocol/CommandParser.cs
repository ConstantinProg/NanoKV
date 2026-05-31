namespace NanoKV.Core.Protocol;

public static class CommandParser
{
    private const byte Space = (byte)' ';
    private const byte CarriageReturn = (byte)'\r';

    public static ParsedCommand Parse(ReadOnlySpan<byte> input)
    {
        input = TrimLine(input);

        if (input.IsEmpty)
            return default;

        int firstSpace = input.IndexOf(Space);

        if (firstSpace < 0)
        {
            CommandType type = GetCommandType(input);

            return new ParsedCommand(
                type,
                ReadOnlySpan<byte>.Empty,
                ReadOnlySpan<byte>.Empty,
                hasCommand: true);
        }

        ReadOnlySpan<byte> command = input[..firstSpace];
        ReadOnlySpan<byte> remainder = TrimLeadingSpaces(input[(firstSpace + 1)..]);

        CommandType commandType = GetCommandType(command);

        if (remainder.IsEmpty)
        {
            return new ParsedCommand(
                commandType,
                ReadOnlySpan<byte>.Empty,
                ReadOnlySpan<byte>.Empty,
                hasCommand: true);
        }

        int secondSpace = remainder.IndexOf(Space);

        if (secondSpace < 0)
        {
            return new ParsedCommand(
                commandType,
                remainder,
                ReadOnlySpan<byte>.Empty,
                hasCommand: true);
        }

        ReadOnlySpan<byte> key = remainder[..secondSpace];
        ReadOnlySpan<byte> value = TrimLeadingSpaces(remainder[(secondSpace + 1)..]);

        return new ParsedCommand(
            commandType,
            key,
            value,
            hasCommand: true);
    }

    private static CommandType GetCommandType(ReadOnlySpan<byte> command)
    {
        if (EqualsAsciiIgnoreCase(command, "SET"u8))
            return CommandType.Set;

        if (EqualsAsciiIgnoreCase(command, "GET"u8))
            return CommandType.Get;

        if (EqualsAsciiIgnoreCase(command, "DELETE"u8))
            return CommandType.Delete;

        if (EqualsAsciiIgnoreCase(command, "STATS"u8))
            return CommandType.Stats;

        return CommandType.Unknown;
    }

    private static bool EqualsAsciiIgnoreCase(
        ReadOnlySpan<byte> left,
        ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length)
            return false;

        for (int i = 0; i < left.Length; i++)
        {
            if (ToUpperAscii(left[i]) != right[i])
                return false;
        }

        return true;
    }

    private static byte ToUpperAscii(byte value)
    {
        return value is >= (byte)'a' and <= (byte)'z'
            ? (byte)(value - 32)
            : value;
    }

    private static ReadOnlySpan<byte> TrimLine(ReadOnlySpan<byte> span)
    {
        span = TrimLeadingSpaces(span);

        int end = span.Length - 1;

        while (end >= 0 && IsTrailingLineWhitespace(span[end]))
            end--;

        return span[..(end + 1)];
    }

    private static ReadOnlySpan<byte> TrimLeadingSpaces(ReadOnlySpan<byte> span)
    {
        int start = 0;

        while (start < span.Length && span[start] == Space)
            start++;

        return span[start..];
    }

    private static bool IsTrailingLineWhitespace(byte value)
    {
        return value is Space or CarriageReturn;
    }
}
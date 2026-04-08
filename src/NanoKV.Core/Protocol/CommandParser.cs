namespace NanoKV.Core.Protocol;

public static class CommandParser
{
    private const byte Space = (byte)' ';

    public static ParsedCommand Parse(ReadOnlySpan<byte> input)
    {
        if (input.IsEmpty)
            return default;

        input = TrimSpaces(input);

        if (input.IsEmpty)
            return default;

        int firstSpace = input.IndexOf(Space);

        if (firstSpace < 0)
        {
            return new ParsedCommand(
                input,
                ReadOnlySpan<byte>.Empty,
                ReadOnlySpan<byte>.Empty);
        }

        var command = input.Slice(0, firstSpace);

        var remainder = TrimSpaces(input.Slice(firstSpace + 1));
        if (remainder.IsEmpty)
        {
            return new ParsedCommand(
                command,
                ReadOnlySpan<byte>.Empty,
                ReadOnlySpan<byte>.Empty);
        }

        int secondSpace = remainder.IndexOf(Space);

        if (secondSpace < 0)
        {
            return new ParsedCommand(command, remainder, ReadOnlySpan<byte>.Empty);
        }

        var key = remainder.Slice(0, secondSpace);
        var value = TrimSpaces(remainder.Slice(secondSpace + 1));

        return new ParsedCommand(command, key, value);
    }

    private static ReadOnlySpan<byte> TrimSpaces(ReadOnlySpan<byte> span)
    {
        int start = 0;
        int end = span.Length - 1;

        while (start <= end && (span[start] == Space || span[start] == '\r'))
            start++;

        while (end >= start && (span[end] == Space || span[end] == '\r'))
            end--;

        return span.Slice(start, end - start + 1);
    }
}

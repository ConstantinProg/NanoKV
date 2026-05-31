using System.Text;
using NanoKV.Core.Protocol;

namespace NanoKV.Core.Tests.Protocol;

public sealed class CommandParserTests
{
    [Fact]
    public void Parse_ReturnsEmptyCommand_WhenInputIsEmpty()
    {
        ParsedCommand command = CommandParser.Parse(ReadOnlySpan<byte>.Empty);

        Assert.True(command.IsEmpty);
        Assert.False(command.HasCommand);
        Assert.Equal(CommandType.Unknown, command.Type);
        Assert.True(command.Key.IsEmpty);
        Assert.True(command.Value.IsEmpty);
    }

    [Fact]
    public void Parse_ReturnsEmptyCommand_WhenInputContainsOnlySpaces()
    {
        byte[] input = Encoding.ASCII.GetBytes("     \r");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.True(command.IsEmpty);
        Assert.False(command.HasCommand);
        Assert.Equal(CommandType.Unknown, command.Type);
    }

    [Fact]
    public void Parse_SetCommand_WithKeyAndValue()
    {
        byte[] input = Encoding.ASCII.GetBytes("SET user:1 hello");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.False(command.IsEmpty);
        Assert.True(command.HasCommand);
        Assert.Equal(CommandType.Set, command.Type);
        AssertSpan("user:1", command.Key);
        AssertSpan("hello", command.Value);
    }

    [Fact]
    public void Parse_SetCommand_PreservesSpacesInsideValue()
    {
        byte[] input = Encoding.ASCII.GetBytes("SET key hello world from NanoKV");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(CommandType.Set, command.Type);
        AssertSpan("key", command.Key);
        AssertSpan("hello world from NanoKV", command.Value);
    }

    [Fact]
    public void Parse_SetCommand_TrimsExtraSpacesBeforeValue()
    {
        byte[] input = Encoding.ASCII.GetBytes("SET key     value");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(CommandType.Set, command.Type);
        AssertSpan("key", command.Key);
        AssertSpan("value", command.Value);
    }

    [Fact]
    public void Parse_SetCommand_WithoutValue()
    {
        byte[] input = Encoding.ASCII.GetBytes("SET key");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(CommandType.Set, command.Type);
        AssertSpan("key", command.Key);
        Assert.True(command.Value.IsEmpty);
    }

    [Fact]
    public void Parse_GetCommand_WithKey()
    {
        byte[] input = Encoding.ASCII.GetBytes("GET user:1");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(CommandType.Get, command.Type);
        AssertSpan("user:1", command.Key);
        Assert.True(command.Value.IsEmpty);
    }

    [Fact]
    public void Parse_GetCommand_WithExtraArgument()
    {
        byte[] input = Encoding.ASCII.GetBytes("GET user:1 extra");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(CommandType.Get, command.Type);
        AssertSpan("user:1", command.Key);
        AssertSpan("extra", command.Value);
    }

    [Fact]
    public void Parse_DeleteCommand_WithKey()
    {
        byte[] input = Encoding.ASCII.GetBytes("DELETE user:1");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(CommandType.Delete, command.Type);
        AssertSpan("user:1", command.Key);
        Assert.True(command.Value.IsEmpty);
    }

    [Fact]
    public void Parse_StatsCommand_WithoutArguments()
    {
        byte[] input = Encoding.ASCII.GetBytes("STATS");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(CommandType.Stats, command.Type);
        Assert.True(command.Key.IsEmpty);
        Assert.True(command.Value.IsEmpty);
    }

    [Fact]
    public void Parse_StatsCommand_WithArguments()
    {
        byte[] input = Encoding.ASCII.GetBytes("STATS extra value");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(CommandType.Stats, command.Type);
        AssertSpan("extra", command.Key);
        AssertSpan("value", command.Value);
    }

    [Theory]
    [InlineData("set key value", CommandType.Set)]
    [InlineData("get key", CommandType.Get)]
    [InlineData("delete key", CommandType.Delete)]
    [InlineData("stats", CommandType.Stats)]
    [InlineData("SeT key value", CommandType.Set)]
    [InlineData("GeT key", CommandType.Get)]
    [InlineData("DeLeTe key", CommandType.Delete)]
    [InlineData("StAtS", CommandType.Stats)]
    public void Parse_CommandType_IsCaseInsensitive(
        string text,
        CommandType expectedType)
    {
        byte[] input = Encoding.ASCII.GetBytes(text);

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(expectedType, command.Type);
    }

    [Fact]
    public void Parse_UnknownCommand_WithoutArguments()
    {
        byte[] input = Encoding.ASCII.GetBytes("PING");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.False(command.IsEmpty);
        Assert.True(command.HasCommand);
        Assert.Equal(CommandType.Unknown, command.Type);
        Assert.True(command.Key.IsEmpty);
        Assert.True(command.Value.IsEmpty);
    }

    [Fact]
    public void Parse_UnknownCommand_WithArguments()
    {
        byte[] input = Encoding.ASCII.GetBytes("PING key value");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(CommandType.Unknown, command.Type);
        AssertSpan("key", command.Key);
        AssertSpan("value", command.Value);
    }

    [Fact]
    public void Parse_TrimsLeadingSpaces()
    {
        byte[] input = Encoding.ASCII.GetBytes("   GET key");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(CommandType.Get, command.Type);
        AssertSpan("key", command.Key);
    }

    [Fact]
    public void Parse_TrimsTrailingCarriageReturn()
    {
        byte[] input = Encoding.ASCII.GetBytes("GET key\r");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(CommandType.Get, command.Type);
        AssertSpan("key", command.Key);
        Assert.True(command.Value.IsEmpty);
    }

    [Fact]
    public void Parse_TrimsTrailingSpacesAndCarriageReturn()
    {
        byte[] input = Encoding.ASCII.GetBytes("GET key   \r");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(CommandType.Get, command.Type);
        AssertSpan("key", command.Key);
        Assert.True(command.Value.IsEmpty);
    }

    [Fact]
    public void Parse_DoesNotTreatTabAsSeparator()
    {
        byte[] input = Encoding.ASCII.GetBytes("GET\tkey");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(CommandType.Unknown, command.Type);
        Assert.True(command.Key.IsEmpty);
        Assert.True(command.Value.IsEmpty);
    }

    [Fact]
    public void Parse_CommandWithoutKey()
    {
        byte[] input = Encoding.ASCII.GetBytes("GET");

        ParsedCommand command = CommandParser.Parse(input);

        Assert.Equal(CommandType.Get, command.Type);
        Assert.True(command.Key.IsEmpty);
        Assert.True(command.Value.IsEmpty);
    }

    private static void AssertSpan(string expected, ReadOnlySpan<byte> actual)
    {
        Assert.Equal(expected, Encoding.ASCII.GetString(actual));
    }
}
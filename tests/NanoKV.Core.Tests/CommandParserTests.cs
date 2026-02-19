using System.Text;
using Xunit;

namespace NanoKv.Core.Protocol;

public class CommandParserTests
{
    [Fact]
    public void Parse_SetCommand_ShouldParseAllParts()
    {
        var input = Encoding.ASCII.GetBytes("SET user:1 data");

        var result = CommandParser.Parse(input);

        Assert.Equal("SET", Encoding.ASCII.GetString(result.Command));
        Assert.Equal("user:1", Encoding.ASCII.GetString(result.Key));
        Assert.Equal("data", Encoding.ASCII.GetString(result.Value));
    }

    [Fact]
    public void Parse_GetCommand_ShouldParseCommandAndKey()
    {
        var input = Encoding.ASCII.GetBytes("GET user:1");

        var result = CommandParser.Parse(input);

        Assert.Equal("GET", Encoding.ASCII.GetString(result.Command));
        Assert.Equal("user:1", Encoding.ASCII.GetString(result.Key));
        Assert.True(result.Value.IsEmpty);
    }

    [Fact]
    public void Parse_InvalidCommand_ShouldReturnEmpty()
    {
        var input = Encoding.ASCII.GetBytes("SET");

        var result = CommandParser.Parse(input);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Parse_CommandWithExtraSpaces_ShouldParseCorrectly()
    {
        var input = Encoding.ASCII.GetBytes("   SET   user:1   data   ");

        var result = CommandParser.Parse(input);

        Assert.Equal("SET", Encoding.ASCII.GetString(result.Command));
        Assert.Equal("user:1", Encoding.ASCII.GetString(result.Key));
        Assert.Equal("data", Encoding.ASCII.GetString(result.Value));
    }
}

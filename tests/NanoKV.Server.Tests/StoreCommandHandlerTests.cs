using NanoKV.Core.Protocol;
using NanoKV.Core.Storage;
using NanoKV.Server;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NanoKV.Tests;

public class StoreCommandHandlerTests
{
    private static ParsedCommand Parse(string input)
    {
        var bytes = Encoding.ASCII.GetBytes(input);
        return CommandParser.Parse(bytes);
    }

    private static string AsString(byte[] response)
    {
        return Encoding.UTF8.GetString(response);
    }

    [Fact]
    public async Task Set_Should_Return_Ok()
    {
        var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var response = await handler.HandleAsync(Parse("SET foo bar"));

        Assert.Equal("OK\r\n", AsString(response));
    }

    [Fact]
    public async Task Get_Existing_Key_Should_Return_Value()
    {
        var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        await handler.HandleAsync(Parse("SET foo bar"));
        var response = await handler.HandleAsync(Parse("GET foo"));

        Assert.Equal("bar\r\n", AsString(response));
    }

    [Fact]
    public async Task Get_Missing_Key_Should_Return_Nil()
    {
        var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var response = await handler.HandleAsync(Parse("GET foo"));

        Assert.Equal("(nil)\r\n", AsString(response));
    }

    [Fact]
    public async Task Delete_Should_Remove_Key()
    {
        var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        await handler.HandleAsync(Parse("SET foo bar"));
        var deleteResponse = await handler.HandleAsync(Parse("DELETE foo"));
        var getResponse = await handler.HandleAsync(Parse("GET foo"));

        Assert.Equal("OK\r\n", AsString(deleteResponse));
        Assert.Equal("(nil)\r\n", AsString(getResponse));
    }

    [Fact]
    public async Task Unknown_Command_Without_Arguments_Should_Return_Error()
    {
        var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var response = await handler.HandleAsync(Parse("PING"));

        Assert.Equal("-ERR Unknown command\r\n", AsString(response));
    }

    [Fact]
    public async Task Set_Without_Arguments_Should_Return_Argument_Error()
    {
        var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var response = await handler.HandleAsync(Parse("SET"));

        Assert.Equal("-ERR wrong number of arguments\r\n", AsString(response));
    }

    [Fact]
    public async Task Set_With_Key_But_Without_Value_Should_Return_Argument_Error()
    {
        var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var response = await handler.HandleAsync(Parse("SET foo"));

        Assert.Equal("-ERR wrong number of arguments\r\n", AsString(response));
    }

    [Fact]
    public async Task Get_Without_Key_Should_Return_Argument_Error()
    {
        var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var response = await handler.HandleAsync(Parse("GET"));

        Assert.Equal("-ERR wrong number of arguments\r\n", AsString(response));
    }

    [Fact]
    public async Task Delete_Without_Key_Should_Return_Argument_Error()
    {
        var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var response = await handler.HandleAsync(Parse("DELETE"));

        Assert.Equal("-ERR wrong number of arguments\r\n", AsString(response));
    }
}
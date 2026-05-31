using NanoKV.Core.Protocol;
using NanoKV.Core.Storage;
using NanoKV.Server;
using System.Text;
using Xunit;

namespace NanoKV.Server.Tests;

public sealed class StoreCommandHandlerTests
{
    [Fact]
    public void Handle_Set_ReturnsOk()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "SET key value");

        Assert.Equal("+OK\r\n", Decode(response));
    }

    [Fact]
    public void Handle_Set_StoresRawValue()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        Handle(handler, "SET key hello world");

        bool found = store.TryGet("key", out byte[]? value);

        Assert.True(found);
        Assert.NotNull(value);
        Assert.Equal("hello world", Decode(value));
    }

    [Fact]
    public void Handle_Set_DoesNotRequireJson()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "SET key plain-text-value");

        Assert.Equal("+OK\r\n", Decode(response));

        Assert.True(store.TryGet("key", out byte[]? value));
        Assert.Equal("plain-text-value", Decode(value));
    }

    [Fact]
    public void Handle_SetWithoutKey_ReturnsError()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "SET");

        Assert.Equal("-ERR SET requires key and non-empty value\r\n", Decode(response));
    }

    [Fact]
    public void Handle_SetWithoutValue_ReturnsError()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "SET key");

        Assert.Equal("-ERR SET requires key and non-empty value\r\n", Decode(response));
    }

    [Fact]
    public void Handle_GetExistingKey_ReturnsBulkString()
    {
        using var store = new SimpleStore();
        store.Set("key", "value"u8);

        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "GET key");

        Assert.Equal("$5\r\nvalue\r\n", Decode(response));
    }

    [Fact]
    public void Handle_GetMissingKey_ReturnsNullBulkString()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "GET missing");

        Assert.Equal("$-1\r\n", Decode(response));
    }

    [Fact]
    public void Handle_GetWithoutKey_ReturnsError()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "GET");

        Assert.Equal("-ERR GET requires exactly one key\r\n", Decode(response));
    }

    [Fact]
    public void Handle_GetWithExtraArgument_ReturnsError()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "GET key extra");

        Assert.Equal("-ERR GET requires exactly one key\r\n", Decode(response));
    }

    [Fact]
    public void Handle_DeleteExistingKey_ReturnsOkAndRemovesValue()
    {
        using var store = new SimpleStore();
        store.Set("key", "value"u8);

        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "DELETE key");

        Assert.Equal("+OK\r\n", Decode(response));
        Assert.False(store.TryGet("key", out _));
    }

    [Fact]
    public void Handle_DeleteMissingKey_ReturnsOk()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "DELETE missing");

        Assert.Equal("+OK\r\n", Decode(response));
    }

    [Fact]
    public void Handle_DeleteWithoutKey_ReturnsError()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "DELETE");

        Assert.Equal("-ERR DELETE requires exactly one key\r\n", Decode(response));
    }

    [Fact]
    public void Handle_DeleteWithExtraArgument_ReturnsError()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "DELETE key extra");

        Assert.Equal("-ERR DELETE requires exactly one key\r\n", Decode(response));
    }

    [Fact]
    public void Handle_Stats_ReturnsBulkString()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        Handle(handler, "SET key value");
        Handle(handler, "GET key");
        Handle(handler, "DELETE key");

        byte[] response = Handle(handler, "STATS");

        Assert.Equal("$23\r\nsets=1;gets=1;deletes=1\r\n", Decode(response));
    }

    [Fact]
    public void Handle_StatsWithArgument_ReturnsError()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "STATS extra");

        Assert.Equal("-ERR STATS does not accept arguments\r\n", Decode(response));
    }

    [Fact]
    public void Handle_UnknownCommand_ReturnsError()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "PING");

        Assert.Equal("-ERR unknown command\r\n", Decode(response));
    }

    [Fact]
    public void Handle_EmptyCommand_ReturnsError()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        byte[] response = Handle(handler, "   ");

        Assert.Equal("-ERR empty command\r\n", Decode(response));
    }

    private static byte[] Handle(StoreCommandHandler handler, string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        ParsedCommand command = CommandParser.Parse(bytes);

        return handler.Handle(command);
    }

    private static string Decode(byte[] value)
    {
        return Encoding.UTF8.GetString(value);
    }
}
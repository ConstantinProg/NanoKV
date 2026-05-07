using NanoKV.Core.Models;
using NanoKV.Core.Protocol;
using NanoKV.Core.Storage;
using NanoKV.Server;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace NanoKV.Tests;

public class StoreCommandHandlerTests
{
    private static ParsedCommand Parse(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return CommandParser.Parse(bytes);
    }

    private static string AsString(byte[] response)
    {
        return Encoding.UTF8.GetString(response);
    }

    private static UserProfile CreateProfile()
    {
        return new UserProfile
        {
            Id = 1,
            Username = "konstantin",
            CreatedAt = new DateTime(2026, 5, 7, 12, 0, 0, DateTimeKind.Utc)
        };
    }

    [Fact]
    public async Task Set_Should_Return_Ok()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var profile = CreateProfile();
        var json = JsonSerializer.Serialize(profile);

        var response = await handler.HandleAsync(Parse($"SET user:1 {json}"));

        Assert.Equal("OK\r\n", AsString(response));
    }

    [Fact]
    public async Task Get_Existing_Key_Should_Return_Profile_As_Json()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var profile = CreateProfile();
        var json = JsonSerializer.Serialize(profile);

        await handler.HandleAsync(Parse($"SET user:1 {json}"));

        var response = await handler.HandleAsync(Parse("GET user:1"));
        var responseText = AsString(response).TrimEnd('\r', '\n');

        var result = JsonSerializer.Deserialize<UserProfile>(responseText);

        Assert.NotNull(result);
        Assert.Equal(profile.Id, result.Id);
        Assert.Equal(profile.Username, result.Username);
        Assert.Equal(profile.CreatedAt, result.CreatedAt);
    }

    [Fact]
    public async Task Get_Missing_Key_Should_Return_Nil()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var response = await handler.HandleAsync(Parse("GET user:1"));

        Assert.Equal("(nil)\r\n", AsString(response));
    }

    [Fact]
    public async Task Delete_Should_Remove_Key()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var profile = CreateProfile();
        var json = JsonSerializer.Serialize(profile);

        await handler.HandleAsync(Parse($"SET user:1 {json}"));

        var deleteResponse = await handler.HandleAsync(Parse("DELETE user:1"));
        var getResponse = await handler.HandleAsync(Parse("GET user:1"));

        Assert.Equal("OK\r\n", AsString(deleteResponse));
        Assert.Equal("(nil)\r\n", AsString(getResponse));
    }

    [Fact]
    public async Task Unknown_Command_Without_Arguments_Should_Return_Error()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var response = await handler.HandleAsync(Parse("PING"));

        Assert.Equal("-ERR Unknown command\r\n", AsString(response));
    }

    [Fact]
    public async Task Set_Without_Arguments_Should_Return_Argument_Error()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var response = await handler.HandleAsync(Parse("SET"));

        Assert.Equal("-ERR wrong number of arguments\r\n", AsString(response));
    }

    [Fact]
    public async Task Set_With_Key_But_Without_Value_Should_Return_Argument_Error()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var response = await handler.HandleAsync(Parse("SET user:1"));

        Assert.Equal("-ERR wrong number of arguments\r\n", AsString(response));
    }

    [Fact]
    public async Task Set_With_Invalid_Json_Should_Return_Invalid_Json_Error()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var response = await handler.HandleAsync(Parse("SET user:1 invalid-json"));

        Assert.Equal("-ERR invalid json\r\n", AsString(response));
    }

    [Fact]
    public async Task Get_Without_Key_Should_Return_Argument_Error()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var response = await handler.HandleAsync(Parse("GET"));

        Assert.Equal("-ERR wrong number of arguments\r\n", AsString(response));
    }

    [Fact]
    public async Task Delete_Without_Key_Should_Return_Argument_Error()
    {
        using var store = new SimpleStore();
        var handler = new StoreCommandHandler(store);

        var response = await handler.HandleAsync(Parse("DELETE"));

        Assert.Equal("-ERR wrong number of arguments\r\n", AsString(response));
    }
}
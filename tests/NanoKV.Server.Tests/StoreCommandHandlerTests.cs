using NanoKV.Core.Storage;
using System;
using System.Text;
using Xunit;

namespace NanoKV.Core.Tests.Storage;

public sealed class SimpleStoreTests
{
    [Fact]
    public void Set_StoresByteValue()
    {
        using var store = new SimpleStore();

        store.Set("key", Encoding.UTF8.GetBytes("value"));

        bool found = store.TryGet("key", out byte[]? value);

        Assert.True(found);
        Assert.NotNull(value);
        Assert.Equal("value", Encoding.UTF8.GetString(value));
    }

    [Fact]
    public void Set_CopiesInputValue()
    {
        using var store = new SimpleStore();

        byte[] source = Encoding.UTF8.GetBytes("value");

        store.Set("key", source);

        source[0] = (byte)'X';

        bool found = store.TryGet("key", out byte[]? value);

        Assert.True(found);
        Assert.Equal("value", Encoding.UTF8.GetString(value!));
    }

    [Fact]
    public void TryGet_ReturnsCopy()
    {
        using var store = new SimpleStore();

        store.Set("key", Encoding.UTF8.GetBytes("value"));

        store.TryGet("key", out byte[]? first);
        first![0] = (byte)'X';

        store.TryGet("key", out byte[]? second);

        Assert.Equal("value", Encoding.UTF8.GetString(second!));
    }

    [Fact]
    public void TryGet_ReturnsFalse_WhenKeyDoesNotExist()
    {
        using var store = new SimpleStore();

        bool found = store.TryGet("missing", out byte[]? value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void Delete_RemovesExistingKey()
    {
        using var store = new SimpleStore();

        store.Set("key", Encoding.UTF8.GetBytes("value"));

        bool deleted = store.Delete("key");
        bool found = store.TryGet("key", out byte[]? value);

        Assert.True(deleted);
        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void Delete_ReturnsFalse_WhenKeyDoesNotExist()
    {
        using var store = new SimpleStore();

        bool deleted = store.Delete("missing");

        Assert.False(deleted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Set_Throws_WhenKeyIsInvalid(string? key)
    {
        using var store = new SimpleStore();

        Assert.ThrowsAny<ArgumentException>(() =>
            store.Set(key!, Encoding.UTF8.GetBytes("value")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void TryGet_Throws_WhenKeyIsInvalid(string? key)
    {
        using var store = new SimpleStore();

        Assert.ThrowsAny<ArgumentException>(() =>
            store.TryGet(key!, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Delete_Throws_WhenKeyIsInvalid(string? key)
    {
        using var store = new SimpleStore();

        Assert.ThrowsAny<ArgumentException>(() =>
            store.Delete(key!));
    }

    [Fact]
    public void GetStatistics_ReturnsOperationCounters()
    {
        using var store = new SimpleStore();

        store.Set("key", Encoding.UTF8.GetBytes("value"));
        store.TryGet("key", out _);
        store.Delete("key");

        StoreStatistics statistics = store.GetStatistics();

        Assert.Equal(1, statistics.SetCount);
        Assert.Equal(1, statistics.GetCount);
        Assert.Equal(1, statistics.DeleteCount);
    }
}
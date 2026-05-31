using System.Text;
using NanoKV.Core.Storage;

namespace NanoKV.Core.Tests;

public class SimpleStoreTests
{
    [Fact]
    public void Set_And_TryGet_Should_Return_Bytes()
    {
        using var store = new SimpleStore();

        byte[] value = Encoding.UTF8.GetBytes("konstantin");

        store.Set("user:1", value);

        bool found = store.TryGet("user:1", out byte[]? result);

        Assert.True(found);
        Assert.NotNull(result);
        Assert.Equal(value, result);
    }

    [Fact]
    public void TryGet_Missing_Key_Should_Return_False()
    {
        using var store = new SimpleStore();

        bool found = store.TryGet("missing", out byte[]? result);

        Assert.False(found);
        Assert.Null(result);
    }

    [Fact]
    public void Delete_Should_Remove_Value()
    {
        using var store = new SimpleStore();

        store.Set("user:1", Encoding.UTF8.GetBytes("user-1"));

        bool deleted = store.Delete("user:1");
        bool found = store.TryGet("user:1", out byte[]? result);

        Assert.True(deleted);
        Assert.False(found);
        Assert.Null(result);
    }

    [Fact]
    public async Task Concurrent_Access_Should_Be_Correct()
    {
        using var store = new SimpleStore();

        const int writers = 10;
        const int readers = 10;
        const int operationsPerTask = 1000;

        var tasks = new Task[writers + readers];

        for (int i = 0; i < writers; i++)
        {
            int writerId = i;

            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < operationsPerTask; j++)
                {
                    byte[] value = Encoding.UTF8.GetBytes($"user-{writerId}-{j}");
                    store.Set($"key-{j}", value);
                }
            });
        }

        for (int i = 0; i < readers; i++)
        {
            tasks[writers + i] = Task.Run(() =>
            {
                for (int j = 0; j < operationsPerTask; j++)
                {
                    store.TryGet($"key-{j}", out _);
                }
            });
        }

        await Task.WhenAll(tasks);

        for (int i = 0; i < operationsPerTask; i++)
        {
            bool found = store.TryGet($"key-{i}", out byte[]? result);

            Assert.True(found);
            Assert.NotNull(result);
        }

        StoreStatistics stats = store.GetStatistics();

        Assert.Equal(writers * operationsPerTask, stats.SetCount);
        Assert.Equal(readers * operationsPerTask + operationsPerTask, stats.GetCount);
    }
}
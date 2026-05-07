using NanoKV.Core.Models;
using NanoKV.Core.Storage;

namespace NanoKV.Core.Tests;

public class SimpleStoreTests
{
    [Fact]
    public void Set_And_Get_Should_Return_UserProfile()
    {
        using var store = new SimpleStore();

        var profile = new UserProfile
        {
            Id = 1,
            Username = "konstantin",
            CreatedAt = new DateTime(2026, 5, 7, 12, 0, 0, DateTimeKind.Utc)
        };

        store.Set("user:1", profile);

        var result = store.Get("user:1");

        Assert.NotNull(result);
        Assert.Equal(profile.Id, result.Id);
        Assert.Equal(profile.Username, result.Username);
        Assert.Equal(profile.CreatedAt, result.CreatedAt);
    }

    [Fact]
    public void Get_Missing_Key_Should_Return_Null()
    {
        using var store = new SimpleStore();

        var result = store.Get("missing");

        Assert.Null(result);
    }

    [Fact]
    public void Delete_Should_Remove_Profile()
    {
        using var store = new SimpleStore();

        var profile = new UserProfile
        {
            Id = 1,
            Username = "user-1",
            CreatedAt = DateTime.UtcNow
        };

        store.Set("user:1", profile);
        store.Delete("user:1");

        var result = store.Get("user:1");

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
                    var profile = new UserProfile
                    {
                        Id = writerId,
                        Username = $"user-{writerId}-{j}",
                        CreatedAt = DateTime.UtcNow
                    };

                    store.Set($"key-{j}", profile);
                }
            });
        }

        for (int i = 0; i < readers; i++)
        {
            tasks[writers + i] = Task.Run(() =>
            {
                for (int j = 0; j < operationsPerTask; j++)
                {
                    store.Get($"key-{j}");
                }
            });
        }

        await Task.WhenAll(tasks);

        for (int i = 0; i < operationsPerTask; i++)
        {
            var result = store.Get($"key-{i}");

            Assert.NotNull(result);
        }

        var stats = store.GetStatistics();

        Assert.Equal(writers * operationsPerTask, stats.setCount);
        Assert.Equal(readers * operationsPerTask + operationsPerTask, stats.getCount);
    }
}
using NanoKV.Core.Storage;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

public class SimpleStoreTests
{
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
            int local = i;
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < operationsPerTask; j++)
                {
                    var key = $"key-{j}";
                    var value = BitConverter.GetBytes(local);

                    store.Set(key, value);
                }
            });
        }

        for (int i = 0; i < readers; i++)
        {
            tasks[writers + i] = Task.Run(() =>
            {
                for (int j = 0; j < operationsPerTask; j++)
                {
                    var key = $"key-{j}";
                    store.Get(key);
                }
            });
        }

        await Task.WhenAll(tasks);

        for (int i = 0; i < operationsPerTask; i++)
        {
            var key = $"key-{i}";
            var value = store.Get(key);

            Assert.NotNull(value);
        }

        var stats = store.GetStatistics();

        Assert.Equal(writers * operationsPerTask, stats.setCount);
        Assert.Equal(readers * operationsPerTask + operationsPerTask, stats.getCount);
    }
}
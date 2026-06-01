using BenchmarkDotNet.Attributes;
using NanoKV.Core.Storage;

namespace NanoKV.Benchmarks;

[Config(typeof(BenchmarkConfig))]
public class SimpleStoreDeleteBenchmarks
{
    enum MyEnum
    {

    }
    private readonly byte[] _value =
        "hello-world-value"u8.ToArray();

    private SimpleStore _store = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _store = new SimpleStore();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _store.Dispose();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _store.Delete("delete-key");
        _store.Set("delete-key", _value);
    }

    [Benchmark]
    public bool Delete()
    {
        return _store.Delete("delete-key");
    }
}
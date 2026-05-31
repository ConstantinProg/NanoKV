using BenchmarkDotNet.Attributes;
using NanoKV.Core.Storage;

namespace NanoKV.Benchmarks;

[Config(typeof(BenchmarkConfig))]
public class SimpleStoreSetBenchmarks
{
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

    [Benchmark]
    public void Set()
    {
        _store.Set("set-key", _value);
    }
}
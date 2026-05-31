using BenchmarkDotNet.Attributes;
using NanoKV.Core.Storage;

namespace NanoKV.Benchmarks;

[Config(typeof(BenchmarkConfig))]
public class SimpleStoreGetBenchmarks
{
    private SimpleStore _store = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _store = new SimpleStore();
        _store.Set("existing-key", "hello-world-value"u8);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _store.Dispose();
    }

    [Benchmark]
    public bool TryGet()
    {
        return _store.TryGet("existing-key", out _);
    }
}
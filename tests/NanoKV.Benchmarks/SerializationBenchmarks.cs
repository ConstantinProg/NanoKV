using System.Text.Json;
using BenchmarkDotNet.Attributes;
using NanoKV.Core.Models;

namespace NanoKV.Benchmarks;

[Config(typeof(BenchmarkConfig))]
public class SerializationBenchmarks
{
    private readonly UserProfile _profile = new()
    {
        Id = 42,
        Username = "constantin",
        CreatedAt = new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc)
    };

    [Benchmark(Baseline = true)]
    public byte[] SystemTextJson()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_profile);
    }

    [Benchmark]
    public byte[] GeneratedBinary()
    {
        return _profile.SerializeToBinary();
    }
}
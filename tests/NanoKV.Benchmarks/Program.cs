using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using NanoKV.Core.Models;
using System.Text.Json;

BenchmarkRunner.Run<SerializationBenchmarks>();

[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private readonly UserProfile _profile = new()
    {
        Id = 42,
        Username = "constantin",
        CreatedAt = new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc)
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
using BenchmarkDotNet.Attributes;
using NanoKV.Core.Protocol;

namespace NanoKV.Benchmarks;

[Config(typeof(BenchmarkConfig))]
public class CommandParserBenchmarks
{
    private readonly byte[] _setCommand =
        "SET user:1 hello-world-value"u8.ToArray();

    private readonly byte[] _getCommand =
        "GET user:1"u8.ToArray();

    [Benchmark]
    public CommandType Parse_Set()
    {
        ParsedCommand command = CommandParser.Parse(_setCommand);

        return command.Type;
    }

    [Benchmark]
    public CommandType Parse_Get()
    {
        ParsedCommand command = CommandParser.Parse(_getCommand);

        return command.Type;
    }
}
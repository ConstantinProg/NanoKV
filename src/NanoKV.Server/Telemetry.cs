using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NanoKV.Server;

public static class Telemetry
{
    public const string ServiceName = "NanoKV.Server";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> CommandsProcessed =
        Meter.CreateCounter<long>(
            name: "nanokv.commands.processed",
            unit: "commands",
            description: "Total number of processed commands.");

    public static readonly Histogram<double> CommandDuration =
        Meter.CreateHistogram<double>(
            name: "nanokv.command.duration",
            unit: "ms",
            description: "Command execution duration in milliseconds.");
}
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NanoKV.Server;

public static class Telemetry
{
    public const string ServiceName = "NanoKV.Server";
    public const string MeterName = ServiceName;
    public const string ActivitySourceName = ServiceName;

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> CommandsProcessed =
        Meter.CreateCounter<long>(
            name: "nanokv.commands.processed",
            unit: "{command}",
            description: "Total number of processed commands.");

    public static readonly Histogram<double> CommandDuration =
        Meter.CreateHistogram<double>(
            name: "nanokv.command.duration",
            unit: "ms",
            description: "Command execution duration in milliseconds.");

    public static readonly UpDownCounter<long> ActiveConnections =
        Meter.CreateUpDownCounter<long>(
            name: "nanokv.connections.active",
            unit: "{connection}",
            description: "Current number of active TCP connections.");

    public static readonly Counter<long> RejectedConnections =
        Meter.CreateCounter<long>(
            name: "nanokv.connections.rejected",
            unit: "{connection}",
            description: "Total number of rejected TCP connections.");

    public static readonly Counter<long> BytesReceived =
        Meter.CreateCounter<long>(
            name: "nanokv.network.bytes_received",
            unit: "By",
            description: "Total number of bytes received from TCP clients.");

    public static readonly Counter<long> BytesSent =
        Meter.CreateCounter<long>(
            name: "nanokv.network.bytes_sent",
            unit: "By",
            description: "Total number of bytes sent to TCP clients.");

    public static ObservableGauge<long> CreateStoreItemCountGauge(
        Func<long> observeValue)
    {
        ArgumentNullException.ThrowIfNull(observeValue);

        return Meter.CreateObservableGauge(
            name: "nanokv.store.items",
            observeValue: observeValue,
            unit: "{item}",
            description: "Current number of items stored in NanoKV.");
    }
}
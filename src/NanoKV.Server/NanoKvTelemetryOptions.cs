namespace NanoKV.Server;

public sealed class NanoKvTelemetryOptions
{
    public bool Enabled { get; init; }

    public bool ConsoleExporterEnabled { get; init; } = true;
}
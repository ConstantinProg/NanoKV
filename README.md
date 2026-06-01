# NanoKV
NanoKV is a lightweight in-memory key-value engine written in modern C#. The project focuses on low-level memory efficiency and zero-allocation parsing techniques.

## Observability

NanoKV supports optional OpenTelemetry tracing and metrics.

Telemetry is disabled by default.

### Enable telemetry

```bash
dotnet run --project src/NanoKV.Server -- --telemetry --logging
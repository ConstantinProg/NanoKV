# NanoKV

NanoKV is a lightweight in-memory key-value server written in modern C#.

The project was created as a practical exercise in high-performance backend development and demonstrates:

* custom TCP protocol;
* asynchronous socket server;
* zero-allocation command parsing with Span<T>;
* thread-safe in-memory storage;
* OpenTelemetry observability;
* Source Generator code generation;
* BenchmarkDotNet microbenchmarks;
* NBomber load testing.

---

# Project Goal

The goal of NanoKV is to implement a small but efficient in-memory key-value database that demonstrates modern .NET performance techniques and backend architecture patterns.

The project focuses on:

* predictable latency;
* low allocation request processing;
* thread-safe concurrent access;
* protocol parsing efficiency;
* observability and diagnostics;
* performance measurement through benchmarks and load testing.

---

# Architecture

## Components Overview

```text
TCP Client
    │
    ▼
TcpServer
    │
    ▼
CommandParser
    │
    ▼
StoreCommandHandler
    │
    ▼
SimpleStore
```

Additional cross-cutting components:

```text
Telemetry (OpenTelemetry)
Benchmarks (BenchmarkDotNet)
Load Tests (NBomber)
Source Generator
```

---

## Network Layer

Responsible for:

* accepting TCP connections;
* limiting concurrent connections;
* idle timeout handling;
* buffering partial TCP packets;
* processing multiple commands in a single receive operation;
* sending protocol responses.

Main class:

```text
TcpServer
```

Features:

* asynchronous sockets;
* connection throttling;
* graceful shutdown;
* ArrayPool<byte> receive buffers;
* TCP stream boundary handling.

---

## Protocol Parser

Responsible for converting incoming bytes into commands.

Main types:

```text
CommandParser
ParsedCommand
CommandType
```

Characteristics:

* Span-based parsing;
* no string allocations during parsing;
* case-insensitive command recognition;
* support for partial TCP receives.

Supported commands:

* SET
* GET
* DELETE
* STATS

---

## Command Pipeline

Command flow:

```text
TcpServer
    →
CommandParser
    →
StoreCommandHandler
    →
SimpleStore
```

Responsibilities:

* command validation;
* protocol error generation;
* storage interaction;
* statistics reporting.

---

## Storage Core

Main type:

```text
SimpleStore
```

Implementation:

```text
Dictionary<string, byte[]>
+
ReaderWriterLockSlim
```

Responsibilities:

* store values;
* retrieve values;
* delete values;
* collect statistics.

Thread safety:

* concurrent reads;
* exclusive writes;
* atomic statistics counters.

---

## Telemetry

NanoKV supports optional OpenTelemetry instrumentation.

Telemetry is disabled by default.

Supported telemetry:

### Traces

```text
nanokv.command.process
```

Tags:

```text
command.name
command.status
command.response.bytes
```

### Metrics

```text
nanokv.commands.processed
nanokv.command.duration
nanokv.connections.active
nanokv.connections.rejected
nanokv.network.bytes_received
nanokv.network.bytes_sent
nanokv.store.items
```

---

## Benchmarks

BenchmarkDotNet benchmarks are included for:

### Parser

```text
CommandParserBenchmarks
```

### Storage

```text
SimpleStoreSetBenchmarks
SimpleStoreGetBenchmarks
SimpleStoreDeleteBenchmarks
```

### Serialization

```text
SerializationBenchmarks
```

---

## Load Tests

NBomber scenarios:

### SET Only

```text
set_only
```

Measures pure write throughput.

### GET After Preloaded Data

```text
get_after_preloaded_data
```

Measures read throughput on existing keys.

### Mixed Workload

```text
mixed_70_get_20_set_10_delete
```

Workload distribution:

```text
70% GET
20% SET
10% DELETE
```

---

# Protocol

NanoKV uses a simple line-oriented protocol.

Each command must end with:

```text
\n
```

---

## SET

Request:

```text
SET user:1 hello
```

Response:

```text
+OK
```

---

## GET

Request:

```text
GET user:1
```

Response:

```text
$5
hello
```

Missing key:

```text
$-1
```

---

## DELETE

Request:

```text
DELETE user:1
```

Response:

```text
+OK
```

---

## STATS

Request:

```text
STATS
```

Response example:

```text
$42
sets=10;gets=20;deletes=3;items=7
```

---

## Errors

Example:

```text
-ERR unknown command
```

Other errors:

```text
-ERR empty command
-ERR invalid key
-ERR command too long
-ERR idle timeout
-ERR too many connections
```

---

# System Design

Request flow:

```text
TCP Client
    │
    ▼
Socket Receive
    │
    ▼
LineBuffer
    │
    ▼
CommandParser
    │
    ▼
StoreCommandHandler
    │
    ▼
SimpleStore
    │
    ▼
ProtocolResponse
    │
    ▼
Socket Send
```

Observability flow:

```text
Command
    │
    ▼
ActivitySource
    │
    ▼
OpenTelemetry
```

Metrics flow:

```text
Command Execution
    │
    ▼
Meter
    │
    ▼
OpenTelemetry Metrics
```

---

# Running the Server

Build:

```bash
dotnet build
```

Run:

```bash
dotnet run --project src/NanoKV.Server
```

Custom port:

```bash
dotnet run --project src/NanoKV.Server -- --port=9000
```

Enable logging:

```bash
dotnet run --project src/NanoKV.Server -- --logging
```

Enable telemetry:

```bash
dotnet run --project src/NanoKV.Server -- --telemetry --logging
```

---

# Manual Testing

## Using telnet

Connect:

```bash
telnet 127.0.0.1 8080
```

Commands:

```text
SET user:1 hello
GET user:1
DELETE user:1
STATS
```

---

## Using netcat

```bash
nc 127.0.0.1 8080
```

Example:

```text
SET user:1 hello
GET user:1
```

---

# Running Unit Tests

Run all tests:

```bash
dotnet test
```

Run Core tests only:

```bash
dotnet test tests/NanoKV.Core.Tests
```

---

# Running BenchmarkDotNet

Run all benchmarks:

```bash
dotnet run -c Release --project tests/NanoKV.Benchmarks
```

Run a specific benchmark:

```bash
dotnet run -c Release --project tests/NanoKV.Benchmarks -- --filter *CommandParser*
```

---

# Running NBomber Load Tests

Start NanoKV server first.

Run all scenarios:

```bash
dotnet run -c Release --project tests/NanoKV.LoadTests
```

Run SET only:

```bash
dotnet run -c Release --project tests/NanoKV.LoadTests set_only
```

Run GET only:

```bash
dotnet run -c Release --project tests/NanoKV.LoadTests get_after_preloaded_data
```

Run mixed workload:

```bash
dotnet run -c Release --project tests/NanoKV.LoadTests mixed_70_get_20_set_10_delete
```

---

# Applied Optimizations

## Span Parser

CommandParser operates on:

```csharp
ReadOnlySpan<byte>
```

Benefits:

* minimal allocations;
* fast parsing;
* reduced GC pressure.

---

## ArrayPool

Used for:

* receive buffers;
* temporary network buffers.

Benefits:

* buffer reuse;
* reduced allocations.

---

## ReaderWriterLockSlim

Used inside SimpleStore.

Benefits:

* multiple concurrent readers;
* exclusive writers;
* efficient read-heavy workloads.

---

## Interlocked

Used for:

* counters;
* statistics collection.

Benefits:

* lock-free atomic updates.

---

## Source Generator

NanoKV.SerializationGenerator generates binary serializers at compile time.

Benefits:

* avoids reflection;
* improves serialization performance;
* provides strongly typed serialization code.

---

## OpenTelemetry

Provides:

* distributed tracing;
* metrics collection;
* runtime observability.

Telemetry can be enabled without modifying application code.

---

# Performance Results

The following measurements were collected on the development machine using BenchmarkDotNet and NBomber.

> These numbers are intended to demonstrate the effectiveness of the implemented optimizations and should not be treated as absolute production performance figures.

---

## Benchmark Environment

```text
BenchmarkDotNet v0.15.2
Windows 11 (10.0.26200.8457)

.NET SDK 10.0.300
.NET Runtime 9.0.16

JIT: RyuJIT
Architecture: x64
Vector ISA: AVX2
```

---

## Command Parser Benchmarks

The parser operates directly on `ReadOnlySpan<byte>` and performs command recognition without allocations.

| Method    |     Mean | Allocated |
| --------- | -------: | --------: |
| Parse_Set | 5.617 ns |       0 B |
| Parse_Get | 5.848 ns |       0 B |

### Observations

* Zero allocations.
* Sub-10 ns parsing latency.
* Suitable for high-throughput request processing.

---

## Storage Benchmarks

### SET

| Method |     Mean | Allocated |
| ------ | -------: | --------: |
| Set    | 26.89 ns |      48 B |

### DELETE

| Method |   Mean | Allocated |
| ------ | -----: | --------: |
| Delete | 900 ns |       0 B |

### Observations

* SET remains extremely fast despite value copying.
* DELETE executes without allocations.
* ReaderWriterLockSlim overhead remains low for the tested workload.

---

## Serialization Benchmarks

Comparison between generated binary serialization and System.Text.Json.

| Method          |     Mean | Ratio | Allocated |
| --------------- | -------: | ----: | --------: |
| SystemTextJson  | 79.53 ns | 1.00x |      96 B |
| GeneratedBinary | 34.93 ns | 0.44x |     432 B |

### Observations

* Generated serializer is approximately **2.3× faster** than System.Text.Json.
* Current implementation still allocates more memory because it creates a new MemoryStream and byte array for every serialization.
* Further optimization is possible through buffer reuse and pooling.

---

## NBomber Load Tests

Configuration:

```text
Virtual Users: 32
Duration: 30 seconds
Warmup: 10 seconds
```

---

### SET Only

Workload:

```text
100% SET
```

| Metric       |      Value |
| ------------ | ---------: |
| Requests     |  2,398,015 |
| Failures     |          0 |
| Throughput   | 79,934 RPS |
| Mean Latency |    0.39 ms |
| P95          |    0.82 ms |
| P99          |    4.21 ms |
| Max          |   93.66 ms |

---

### GET After Preloaded Data

Workload:

```text
100% GET
```

| Metric       |      Value |
| ------------ | ---------: |
| Requests     |  2,886,521 |
| Failures     |          0 |
| Throughput   | 96,217 RPS |
| Mean Latency |    0.33 ms |
| P95          |    0.48 ms |
| P99          |    3.78 ms |
| Max          |   93.65 ms |

---

### Mixed Workload

Workload:

```text
70% GET
20% SET
10% DELETE
```

| Metric       |      Value |
| ------------ | ---------: |
| Requests     |  2,726,939 |
| Failures     |          0 |
| Throughput   | 90,898 RPS |
| Mean Latency |    0.35 ms |
| P95          |    0.53 ms |
| P99          |    3.90 ms |
| Max          |   93.74 ms |

---

## Load Test Summary

| Scenario       |    RPS | Mean Latency |     P95 |     P99 |
| -------------- | -----: | -----------: | ------: | ------: |
| SET Only       | 79,934 |      0.39 ms | 0.82 ms | 4.21 ms |
| GET Only       | 96,217 |      0.33 ms | 0.48 ms | 3.78 ms |
| Mixed 70/20/10 | 90,898 |      0.35 ms | 0.53 ms | 3.90 ms |

### Key Results

* Nearly **100,000 requests/sec** achieved on read-heavy workloads.
* Stable sub-millisecond average latency across all scenarios.
* No failures during the benchmark runs.
* P95 latency remained below **1 ms** in all scenarios.
* Demonstrates effectiveness of:

  * Span-based parsing;
  * ArrayPool buffer reuse;
  * ReaderWriterLockSlim for concurrent access;
  * Interlocked counters;
  * lightweight TCP protocol.

# Known Limitations

Current implementation intentionally keeps the design simple.

Limitations:

* data is stored only in memory;
* data is lost after process restart;
* single-node deployment only;
* no persistence;
* no replication;
* no authentication;
* no TLS encryption;
* no expiration/TTL support;
* no transactions;
* no clustering;
* protocol is not Redis-compatible.

---

# Future Improvements

Possible future enhancements:

* persistent storage engine;
* write-ahead log (WAL);
* snapshot persistence;
* TTL support;
* pipelining;
* batching;
* RESP protocol compatibility;
* authentication and authorization;
* TLS support;
* sharding;
* replication;
* distributed cluster mode;
* System.IO.Pipelines integration;
* lock-free storage structures;
* OpenTelemetry OTLP exporter support.

---

# Technologies

* .NET 9
* C#
* TCP Sockets
* OpenTelemetry
* BenchmarkDotNet
* NBomber
* Roslyn Source Generators
* xUnit
* ReaderWriterLockSlim
* ArrayPool<T>
* Span<T>
* Interlocked

---

# Educational Purpose

NanoKV is an educational project demonstrating how to build a high-performance TCP service in modern .NET using low-level networking, memory-efficient parsing techniques, observability tooling, benchmarking, and load testing.

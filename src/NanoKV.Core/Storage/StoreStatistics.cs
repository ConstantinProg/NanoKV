namespace NanoKV.Core.Storage;

public readonly record struct StoreStatistics(
    long SetCount,
    long GetCount,
    long DeleteCount,
    long ItemCount);
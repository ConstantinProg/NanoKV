using System.Text.Json;
using NanoKV.Core.Models;

namespace NanoKV.Core.Storage;

public sealed class SimpleStore : IDisposable
{
    private readonly Dictionary<string, byte[]> _storage = new();
    private readonly ReaderWriterLockSlim _lock = new();

    private long _setCount;
    private long _getCount;
    private long _deleteCount;

    public void Set(string key, UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(profile);

        _lock.EnterWriteLock();

        try
        {
            _storage[key] = bytes;

            Interlocked.Increment(ref _setCount);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public UserProfile? Get(string key)
    {
        _lock.EnterReadLock();

        try
        {
            if (!_storage.TryGetValue(key, out byte[]? bytes))
                return null;

            Interlocked.Increment(ref _getCount);

            return JsonSerializer.Deserialize<UserProfile>(bytes);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Delete(string key)
    {
        _lock.EnterWriteLock();

        try
        {
            _storage.Remove(key);

            Interlocked.Increment(ref _deleteCount);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public (long setCount, long getCount, long deleteCount) GetStatistics()
    {
        return (_setCount, _getCount, _deleteCount);
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
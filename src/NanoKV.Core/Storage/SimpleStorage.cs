namespace NanoKV.Core.Storage;

public sealed class SimpleStore : IDisposable
{
    private readonly Dictionary<string, byte[]> _storage = new();
    private readonly ReaderWriterLockSlim _lock = new();

    private long _setCount;
    private long _getCount;
    private long _deleteCount;

    public void Set(string key, ReadOnlySpan<byte> value)
    {
        ValidateKey(key);

        byte[] copy = value.ToArray();

        _lock.EnterWriteLock();

        try
        {
            _storage[key] = copy;
            Interlocked.Increment(ref _setCount);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool TryGet(string key, out byte[]? value)
    {
        ValidateKey(key);

        _lock.EnterReadLock();

        try
        {
            Interlocked.Increment(ref _getCount);

            if (!_storage.TryGetValue(key, out byte[]? stored))
            {
                value = null;
                return false;
            }

            value = stored.ToArray();
            return true;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool Delete(string key)
    {
        ValidateKey(key);

        _lock.EnterWriteLock();

        try
        {
            bool removed = _storage.Remove(key);

            if (removed)
                Interlocked.Increment(ref _deleteCount);

            return removed;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public StoreStatistics GetStatistics()
    {
        return new StoreStatistics(
            Interlocked.Read(ref _setCount),
            Interlocked.Read(ref _getCount),
            Interlocked.Read(ref _deleteCount));
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
    }
}
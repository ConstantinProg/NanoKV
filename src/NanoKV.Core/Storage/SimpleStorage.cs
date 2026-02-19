namespace NanoKV.Core.Storage;

public sealed class SimpleStore
{
    private readonly Dictionary<string, byte[]> _storage = new();

    public void Set(string key, byte[] value)
    {
        _storage[key] = value;
    }

    public byte[]? Get(string key)
    {
        return _storage.TryGetValue(key, out var value)
            ? value
            : null;
    }

    public void Delete(string key)
    {
        _storage.Remove(key);
    }
}

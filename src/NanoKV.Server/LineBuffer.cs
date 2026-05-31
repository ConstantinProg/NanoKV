using System.Buffers;

namespace NanoKV.Server;

internal sealed class LineBuffer : IDisposable
{
    private readonly byte[] _buffer;
    private int _length;

    public LineBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _buffer = ArrayPool<byte>.Shared.Rent(capacity);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Length => _length;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _length);

    public bool TryAppend(ReadOnlySpan<byte> data)
    {
        if (data.Length > Capacity - _length)
            return false;

        data.CopyTo(_buffer.AsSpan(_length));
        _length += data.Length;

        return true;
    }

    public void Clear()
    {
        _length = 0;
    }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
    }
}
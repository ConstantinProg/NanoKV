namespace NanoKV.Server;

public sealed class RingBuffer
{
    private readonly byte[] _buffer;
    private int _head;
    private int _tail;
    private int _count;

    public RingBuffer(int capacity)
    {
        _buffer = new byte[capacity];
    }

    public int Count => _count;
    public int Capacity => _buffer.Length;

    public void Write(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            if (_count == _buffer.Length)
                throw new InvalidOperationException("Buffer overflow");

            _buffer[_tail] = b;
            _tail = (_tail + 1) % _buffer.Length;
            _count++;
        }
    }

    public bool TryReadLine(out byte[] line)
    {
        if (_count == 0)
        {
            line = default!;
            return false;
        }

        int index = _head;

        for (int i = 0; i < _count; i++)
        {
            if (_buffer[index] == (byte)'\n')
            {
                int length = i;

                var result = new byte[length];

                for (int j = 0; j < length; j++)
                {
                    result[j] = _buffer[_head];
                    _head = (_head + 1) % _buffer.Length;
                }

                // skip '\n'
                _head = (_head + 1) % _buffer.Length;

                _count -= (length + 1);

                line = result;
                return true;
            }

            index = (index + 1) % _buffer.Length;
        }

        line = default!;
        return false;
    }
}
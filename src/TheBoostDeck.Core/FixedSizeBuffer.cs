namespace TheBoostDeck.Core;

public sealed class FixedSizeBuffer<T>
{
    private readonly object _sync = new();
    private readonly T[] _items;
    private int _next;
    private int _count;

    public FixedSizeBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count
    {
        get
        {
            lock (_sync)
                return _count;
        }
    }

    public void Add(T item)
    {
        lock (_sync)
        {
            _items[_next] = item;
            _next = (_next + 1) % _items.Length;
            _count = Math.Min(_count + 1, _items.Length);
        }
    }

    public IReadOnlyList<T> Snapshot()
    {
        lock (_sync)
        {
            var result = new T[_count];
            var start = (_next - _count + _items.Length) % _items.Length;
            for (var index = 0; index < _count; index++)
                result[index] = _items[(start + index) % _items.Length];
            return result;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            Array.Clear(_items);
            _next = 0;
            _count = 0;
        }
    }
}

using System.Collections.Generic;

namespace Game.Sim;

// Binary min-heap of longs. Callers pack (priority, payload) into one key, so equal
// priorities pop in payload order and the result never depends on insertion order.
internal sealed class MinHeap
{
    private readonly List<long> _items = new List<long>();

    public int Count => _items.Count;

    public void Clear() => _items.Clear();

    public void Push(long key)
    {
        _items.Add(key);
        int i = _items.Count - 1;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (_items[parent] <= key)
                break;
            _items[i] = _items[parent];
            i = parent;
        }
        _items[i] = key;
    }

    public long Pop()
    {
        long top = _items[0];
        long last = _items[_items.Count - 1];
        _items.RemoveAt(_items.Count - 1);
        if (_items.Count == 0)
            return top;

        int i = 0;
        while (true)
        {
            int child = 2 * i + 1;
            if (child >= _items.Count)
                break;
            if (child + 1 < _items.Count && _items[child + 1] < _items[child])
                child++;
            if (_items[child] >= last)
                break;
            _items[i] = _items[child];
            i = child;
        }
        _items[i] = last;
        return top;
    }
}

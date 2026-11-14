namespace Game.Sim;

// Binary min-heap of longs. Callers pack (priority, payload) into one key, so equal
// priorities pop in payload order and the result never depends on insertion order.
// A plain array: path search pushes and pops hundreds of thousands of keys per field.
internal sealed class MinHeap
{
    private long[] _items = new long[256];

    public int Count { get; private set; }

    public void Clear() => Count = 0;

    public void Push(long key)
    {
        if (Count == _items.Length)
            System.Array.Resize(ref _items, _items.Length * 2);
        long[] items = _items;
        int i = Count++;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (items[parent] <= key)
                break;
            items[i] = items[parent];
            i = parent;
        }
        items[i] = key;
    }

    public long Pop()
    {
        long[] items = _items;
        long top = items[0];
        long last = items[--Count];
        int count = Count;
        if (count == 0)
            return top;

        int i = 0;
        while (true)
        {
            int child = 2 * i + 1;
            if (child >= count)
                break;
            if (child + 1 < count && items[child + 1] < items[child])
                child++;
            if (items[child] >= last)
                break;
            items[i] = items[child];
            i = child;
        }
        items[i] = last;
        return top;
    }
}

using System.Collections.Generic;

namespace Game.Sim;

// Flow fields by goal. Derived data: rebuilt from the map on demand, never hashed.
// Many units sharing a goal (a group move, workers returning to the same HQ) share one field.
internal sealed class FlowFieldCache
{
    private readonly SortedDictionary<long, FlowField> _fields = new SortedDictionary<long, FlowField>();
    private readonly MinHeap _heap = new MinHeap();
    private readonly List<int> _goals = new List<int>();
    private int _version = -1;

    // To a single cell; if it is not passable, to the nearest passable cells around it.
    public FlowField ToCell(MapState map, int x, int y)
    {
        Sync(map);
        long key = map.Index(x, y);
        if (_fields.TryGetValue(key, out FlowField? field))
            return field;

        _goals.Clear();
        if (map.IsPassable(x, y))
        {
            _goals.Add(map.Index(x, y));
        }
        else
        {
            for (int ring = 1; _goals.Count == 0 && ring <= map.Width + map.Height; ring++)
                AddRing(map, x, y, 1, 1, ring);
        }
        return Store(map, key);
    }

    // To any passable cell touching the rectangle (a building footprint or a planned site).
    public FlowField ToRectangle(MapState map, int x, int y, int w, int h)
    {
        Sync(map);
        long key = -1 - (x + ((long)y << 14) + ((long)w << 28) + ((long)h << 42));
        if (_fields.TryGetValue(key, out FlowField? field))
            return field;

        _goals.Clear();
        AddRing(map, x, y, w, h, 1);
        return Store(map, key);
    }

    private FlowField Store(MapState map, long key)
    {
        _goals.Sort();
        var field = new FlowField(map, _goals, _heap);
        _fields.Add(key, field);
        return field;
    }

    private void AddRing(MapState map, int x, int y, int w, int h, int ring)
    {
        for (int cy = y - ring; cy <= y + h - 1 + ring; cy++)
        {
            for (int cx = x - ring; cx <= x + w - 1 + ring; cx++)
            {
                bool onRing = cy == y - ring || cy == y + h - 1 + ring || cx == x - ring || cx == x + w - 1 + ring;
                if (onRing && map.IsPassable(cx, cy))
                    _goals.Add(map.Index(cx, cy));
            }
        }
    }

    private void Sync(MapState map)
    {
        if (_version == map.Version)
            return;
        _fields.Clear();
        _version = map.Version;
    }
}

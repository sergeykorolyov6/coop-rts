using System.Collections.Generic;

namespace Game.Sim;

// Buildings by map bucket, so "what stands near this point" does not walk every belt of the
// match. Derived data: rebuilt from the building list when it changes, never hashed.
// A building lies in every bucket its footprint touches, so a search may meet it more than
// once; searches pick by (distance, id), which does not care.
internal sealed class BuildingIndex
{
    // 8×8 cells: a unit's sight covers a handful of buckets.
    private const int Shift = 3;

    private readonly int _columns;
    private readonly int _rows;
    private readonly List<Building>[] _buckets;
    private int _stamp = -1;

    public BuildingIndex(int mapWidth, int mapHeight)
    {
        _columns = ((mapWidth - 1) >> Shift) + 1;
        _rows = ((mapHeight - 1) >> Shift) + 1;
        _buckets = new List<Building>[_columns * _rows];
        for (int i = 0; i < _buckets.Length; i++)
            _buckets[i] = new List<Building>();
    }

    // Bucket range covering the square of the radius around a point, all in milli-cells.
    public void Range(int x, int y, long radius, out int minBx, out int minBy, out int maxBx, out int maxBy)
    {
        minBx = Clamp(Grid.MilliToCell((int)(x - radius)) >> Shift, _columns);
        maxBx = Clamp(Grid.MilliToCell((int)(x + radius)) >> Shift, _columns);
        minBy = Clamp(Grid.MilliToCell((int)(y - radius)) >> Shift, _rows);
        maxBy = Clamp(Grid.MilliToCell((int)(y + radius)) >> Shift, _rows);
    }

    // Buildings touching the bucket, in id order. Dead ones stay until the end of the tick.
    public List<Building> Bucket(SimState state, int bx, int by)
    {
        if (_stamp != state.BuildingListVersion)
            Rebuild(state);
        return _buckets[by * _columns + bx];
    }

    private void Rebuild(SimState state)
    {
        foreach (List<Building> bucket in _buckets)
            bucket.Clear();
        // state.Buildings is sorted by id, so every bucket is too.
        foreach (Building b in state.Buildings)
        {
            int x0 = b.CellX >> Shift, x1 = (b.CellX + b.Width - 1) >> Shift;
            int y0 = b.CellY >> Shift, y1 = (b.CellY + b.Height - 1) >> Shift;
            for (int by = y0; by <= y1; by++)
            {
                for (int bx = x0; bx <= x1; bx++)
                    _buckets[by * _columns + bx].Add(b);
            }
        }
        _stamp = state.BuildingListVersion;
    }

    private static int Clamp(int value, int count) => value < 0 ? 0 : value >= count ? count - 1 : value;
}

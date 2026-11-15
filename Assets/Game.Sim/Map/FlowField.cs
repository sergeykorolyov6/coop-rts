using System.Collections.Generic;

namespace Game.Sim;

// Distance to the nearest goal cell for every cell of the map; units walk downhill.
// Integer costs: 10 per straight step, 14 per diagonal (≈ 10·√2). Diagonals may not cut
// a blocked corner, so a unit never clips through the edge of a building, and no step crosses
// a cliff (heights.md, MapState.CanStep).
public sealed class FlowField
{
    public const int Unreachable = int.MaxValue;
    public const int StraightCost = 10;
    public const int DiagonalCost = 14;

    // Neighbour order is fixed: ties between equal distances always resolve the same way.
    // The first four are the side neighbours, the rest the diagonals.
    internal static readonly int[] Dx = { 1, 0, -1, 0, 1, -1, -1, 1 };
    internal static readonly int[] Dy = { 0, 1, 0, -1, 1, 1, -1, -1 };

    private readonly int[] _dist;
    private readonly int _width;
    private readonly MapState _map;
    private readonly MinHeap _heap = new MinHeap();

    // Distance of the last cell taken off the heap: every cell at or below it is final.
    private int _frontier;

    // The search runs outwards from the goals only as far as somebody asks (Settle): a unit
    // chasing a target three cells away pays for a handful of cells, not for the whole map.
    // Cells come off the heap in the same order as in a full search, so the answers are the same.
    internal FlowField(MapState map, IReadOnlyList<int> goalCells)
    {
        _map = map;
        _width = map.Width;
        Version = map.Version;
        _dist = new int[map.Width * map.Height];
        for (int i = 0; i < _dist.Length; i++)
            _dist[i] = Unreachable;

        foreach (int goal in goalCells)
        {
            if (_dist[goal] != 0)
            {
                _dist[goal] = 0;
                _heap.Push(goal);
            }
        }
    }

    // Map version the field was built for; stale once passability changes.
    public int Version { get; }

    public int DistanceAt(int x, int y)
    {
        int cell = y * _width + x;
        Settle(cell);
        return _dist[cell];
    }

    // Runs the search until the cell's distance is final. A tentative distance equal to the
    // frontier is final too: nothing left on the heap is nearer, and every step costs more than zero.
    private void Settle(int target)
    {
        MapState map = _map;
        // Key = distance << 32 | cell: pops by distance, then by cell index.
        while (_heap.Count > 0 && _dist[target] > _frontier)
        {
            long key = _heap.Pop();
            int cell = (int)(key & 0xFFFFFFFF);
            int dist = (int)(key >> 32);
            if (dist > _dist[cell])
                continue;
            _frontier = dist;

            int cx = cell % _width, cy = cell / _width;
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + Dx[d], ny = cy + Dy[d];
                if (!map.InBounds(nx, ny))
                    continue;
                // Most neighbours are already as near as this step would make them: ask the
                // map about passability only for the few that are not.
                bool diagonal = d >= 4;
                int next = map.Index(nx, ny);
                int nd = dist + (diagonal ? DiagonalCost : StraightCost);
                if (nd >= _dist[next] || !map.IsPassable(nx, ny))
                    continue;
                // The unit will walk the other way, from the neighbour into this cell.
                if (!map.CanStep(nx, ny, cx, cy))
                    continue;
                _dist[next] = nd;
                _heap.Push(((long)nd << 32) | (uint)next);
            }
        }
    }

    // The neighbour to step into from (x, y), or false when (x, y) is a goal or cut off.
    internal bool TryNextCell(MapState map, int x, int y, out int nextX, out int nextY)
    {
        nextX = x;
        nextY = y;
        // Neighbours nearer than this cell left the heap before it, so their distances are final;
        // the rest are no nearer and are never chosen.
        int here = map.InBounds(x, y) ? DistanceAt(x, y) : Unreachable;
        if (here == 0)
            return false;

        int best = here;
        for (int d = 0; d < 8; d++)
        {
            int nx = x + Dx[d], ny = y + Dy[d];
            if (!map.InBounds(nx, ny))
                continue;
            if (!map.CanStep(x, y, nx, ny))
                continue;
            int nd = _dist[map.Index(nx, ny)];
            if (nd < best)
            {
                best = nd;
                nextX = nx;
                nextY = ny;
            }
        }
        return best < here;
    }
}

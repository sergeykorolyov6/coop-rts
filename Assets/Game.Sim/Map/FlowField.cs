using System.Collections.Generic;

namespace Game.Sim;

// Distance to the nearest goal cell for every cell of the map; units walk downhill.
// Integer costs: 10 per straight step, 14 per diagonal (≈ 10·√2). Diagonals may not cut
// a blocked corner, so a unit never clips through the edge of a building.
public sealed class FlowField
{
    public const int Unreachable = int.MaxValue;
    public const int StraightCost = 10;
    public const int DiagonalCost = 14;

    // Neighbour order is fixed: ties between equal distances always resolve the same way.
    internal static readonly int[] Dx = { 1, 0, -1, 0, 1, -1, -1, 1 };
    internal static readonly int[] Dy = { 0, 1, 0, -1, 1, 1, -1, -1 };

    private readonly int[] _dist;
    private readonly int _width;

    internal FlowField(MapState map, IReadOnlyList<int> goalCells, MinHeap heap)
    {
        _width = map.Width;
        Version = map.Version;
        _dist = new int[map.Width * map.Height];
        for (int i = 0; i < _dist.Length; i++)
            _dist[i] = Unreachable;

        heap.Clear();
        foreach (int goal in goalCells)
        {
            if (_dist[goal] != 0)
            {
                _dist[goal] = 0;
                heap.Push(goal);
            }
        }

        // Key = distance << 32 | cell: pops by distance, then by cell index.
        while (heap.Count > 0)
        {
            long key = heap.Pop();
            int cell = (int)(key & 0xFFFFFFFF);
            int dist = (int)(key >> 32);
            if (dist > _dist[cell])
                continue;

            int cx = cell % _width, cy = cell / _width;
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + Dx[d], ny = cy + Dy[d];
                if (!map.IsPassable(nx, ny))
                    continue;
                bool diagonal = d >= 4;
                if (diagonal && (!map.IsPassable(cx + Dx[d], cy) || !map.IsPassable(cx, cy + Dy[d])))
                    continue;
                int next = map.Index(nx, ny);
                int nd = dist + (diagonal ? DiagonalCost : StraightCost);
                if (nd < _dist[next])
                {
                    _dist[next] = nd;
                    heap.Push(((long)nd << 32) | (uint)next);
                }
            }
        }
    }

    // Map version the field was built for; stale once buildings appear or disappear.
    public int Version { get; }

    public int DistanceAt(int x, int y) => _dist[y * _width + x];

    // The neighbour to step into from (x, y), or false when (x, y) is a goal or cut off.
    internal bool TryNextCell(MapState map, int x, int y, out int nextX, out int nextY)
    {
        nextX = x;
        nextY = y;
        int here = map.InBounds(x, y) ? _dist[map.Index(x, y)] : Unreachable;
        if (here == 0)
            return false;

        int best = here;
        for (int d = 0; d < 8; d++)
        {
            int nx = x + Dx[d], ny = y + Dy[d];
            if (!map.InBounds(nx, ny))
                continue;
            if (d >= 4 && (!map.IsPassable(x + Dx[d], y) || !map.IsPassable(x, y + Dy[d])))
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

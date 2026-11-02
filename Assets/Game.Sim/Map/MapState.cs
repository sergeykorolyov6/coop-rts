using System.Collections.Generic;

namespace Game.Sim;

// Per-cell state of the map during the match: terrain, ore and which building stands where.
public sealed class MapState
{
    private readonly bool[] _blocked;
    private readonly int[] _ore;
    private readonly int[] _buildingAt;
    private readonly bool[] _walkableBuilding;
    private readonly int[] _depositAt;
    private readonly List<Deposit> _deposits = new List<Deposit>();

    internal MapState(MapSetup setup)
    {
        Width = setup.Width;
        Height = setup.Height;
        int count = Width * Height;
        _blocked = new bool[count];
        _ore = new int[count];
        _buildingAt = new int[count];
        _walkableBuilding = new bool[count];
        _depositAt = new int[count];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                _blocked[y * Width + x] = setup.IsBlocked(x, y);
                _ore[y * Width + x] = setup.OreAt(x, y);
            }
        }
        FindDeposits();
    }

    public int Width { get; }
    public int Height { get; }

    // Bumped whenever passability changes; cached flow fields compare against it.
    public int Version { get; private set; }

    public IReadOnlyList<Deposit> Deposits => _deposits;

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public int Index(int x, int y) => y * Width + x;

    public bool IsBlocked(int x, int y) => _blocked[Index(x, y)];

    // Resource type of ore in the cell, or MapSetup.NoOre.
    public int OreAt(int x, int y) => _ore[Index(x, y)];

    // Deposit id of the cell, 0 when it is not ore.
    public int DepositAt(int x, int y) => _depositAt[Index(x, y)];

    // Building id standing on the cell, 0 when free.
    public int BuildingAt(int x, int y) => _buildingAt[Index(x, y)];

    // Ground units can stand here: terrain is open and no solid building occupies it.
    public bool IsPassable(int x, int y)
    {
        if (!InBounds(x, y))
            return false;
        int i = Index(x, y);
        return !_blocked[i] && (_buildingAt[i] == 0 || _walkableBuilding[i]);
    }

    // Free for a new building: open terrain, no building of anyone.
    public bool IsBuildable(int x, int y) =>
        InBounds(x, y) && !_blocked[Index(x, y)] && _buildingAt[Index(x, y)] == 0;

    internal void Occupy(Building building)
    {
        bool walkable = BuildingKinds.IsWalkable(building.Stats.Id);
        for (int y = building.CellY; y < building.CellY + building.Height; y++)
        {
            for (int x = building.CellX; x < building.CellX + building.Width; x++)
            {
                _buildingAt[Index(x, y)] = building.Id;
                _walkableBuilding[Index(x, y)] = walkable;
            }
        }
        Version++;
    }

    internal void Free(Building building)
    {
        for (int y = building.CellY; y < building.CellY + building.Height; y++)
        {
            for (int x = building.CellX; x < building.CellX + building.Width; x++)
            {
                if (_buildingAt[Index(x, y)] == building.Id)
                {
                    _buildingAt[Index(x, y)] = 0;
                    _walkableBuilding[Index(x, y)] = false;
                }
            }
        }
        Version++;
    }

    // Deposits are 4-connected patches of one resource type, numbered in row-major order
    // of their first cell, so every machine gets the same ids.
    private void FindDeposits()
    {
        var stack = new List<int>();
        for (int start = 0; start < _ore.Length; start++)
        {
            if (_ore[start] == MapSetup.NoOre || _depositAt[start] != 0)
                continue;

            int id = _deposits.Count + 1;
            int type = _ore[start];
            var cells = new List<int>();
            stack.Add(start);
            _depositAt[start] = id;
            while (stack.Count > 0)
            {
                int cell = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                cells.Add(cell);
                int cx = cell % Width, cy = cell / Width;
                TryVisit(cx + 1, cy);
                TryVisit(cx - 1, cy);
                TryVisit(cx, cy + 1);
                TryVisit(cx, cy - 1);
            }
            cells.Sort();
            _deposits.Add(new Deposit(id, type, cells));

            void TryVisit(int x, int y)
            {
                if (!InBounds(x, y))
                    return;
                int i = Index(x, y);
                if (_ore[i] != type || _depositAt[i] != 0)
                    return;
                _depositAt[i] = id;
                stack.Add(i);
            }
        }
    }
}

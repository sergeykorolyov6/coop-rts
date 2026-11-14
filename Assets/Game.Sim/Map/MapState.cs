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
    private readonly TerrainStats?[] _terrain;
    private readonly byte[] _level;
    private readonly bool[] _ramp;
    private readonly int[] _rockHp;
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
        _terrain = new TerrainStats?[count];
        _level = new byte[count];
        _ramp = new bool[count];
        _rockHp = new int[count];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                _blocked[y * Width + x] = setup.IsBlocked(x, y);
                _ore[y * Width + x] = setup.OreAt(x, y);
                _terrain[y * Width + x] = setup.TerrainAt(x, y);
                _level[y * Width + x] = (byte)setup.LevelAt(x, y);
                _ramp[y * Width + x] = setup.IsRamp(x, y);
                HasLevels |= _level[y * Width + x] != 0 || _ramp[y * Width + x];
                TerrainStats? terrain = _terrain[y * Width + x];
                if (terrain != null && terrain.Destructible)
                    _rockHp[y * Width + x] = terrain.Hp;
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

    // The cell's kind from terrain.csv; null for plain ground and for rock a test set by hand.
    public TerrainStats? TerrainAt(int x, int y) => _terrain[Index(x, y)];

    // Height level, 0..MapSetup.MaxLevel, and whether the cell is a ramp to the level above.
    // heights.md. A ramp of level L lies between L and L + 1.
    public int LevelAt(int x, int y) => _level[Index(x, y)];
    public bool IsRamp(int x, int y) => _ramp[Index(x, y)];

    // False on a flat map: every height rule is skipped.
    public bool HasLevels { get; }

    // What a unit standing in the cell sees over: from a ramp, the plateau it leads to.
    public int EyeLevel(int x, int y) => _level[Index(x, y)] + (_ramp[Index(x, y)] ? 1 : 0);

    // heights.md, "рядом по высоте": no cliff between the levels of the two cells (which need not
    // be neighbours) — equal levels, or a ramp that joins exactly these two.
    public bool LevelsJoin(int ax, int ay, int bx, int by)
    {
        if (!HasLevels)
            return true;
        int a = Index(ax, ay), b = Index(bx, by);
        int la = _level[a], lb = _level[b];
        if (_ramp[a] && _ramp[b])
            return la - lb <= 1 && lb - la <= 1;
        if (_ramp[a])
            return lb == la || lb == la + 1;
        if (_ramp[b])
            return la == lb || la == lb + 1;
        return la == lb;
    }

    // heights.md: a step from a cell into one of its 8 neighbours. The neighbour is passable and
    // there is no cliff on the way; diagonally both ways round must be open as well, so a unit
    // cuts neither the corner of a building nor the corner of a cliff. The cell stepped from may
    // be blocked itself (a unit caught under a new building walks out).
    public bool CanStep(int fromX, int fromY, int toX, int toY)
    {
        if (!IsPassable(toX, toY))
            return false;
        bool diagonal = fromX != toX && fromY != toY;
        if (diagonal && (!IsPassable(toX, fromY) || !IsPassable(fromX, toY)))
            return false;
        if (!HasLevels || !InBounds(fromX, fromY))
            return true;
        if (!LevelsJoin(fromX, fromY, toX, toY))
            return false;
        return !diagonal
            || (LevelsJoin(fromX, fromY, toX, fromY) && LevelsJoin(toX, fromY, toX, toY)
                && LevelsJoin(fromX, fromY, fromX, toY) && LevelsJoin(fromX, toY, toX, toY));
    }

    // terrain.md: what is left of destructible rock, 0 for anything else.
    public int RockHp(int x, int y) => _rockHp[Index(x, y)];

    public bool IsDestructibleRock(int x, int y) => InBounds(x, y) && _rockHp[Index(x, y)] > 0;

    // Rock of any kind stops sight; the rock cell itself is seen (terrain.md, decision 111).
    public bool IsOpaque(int x, int y) => _blocked[Index(x, y)];

    // A fold of every rock cell's damage, for StateHash: equal on every machine that dug the same.
    public ulong RockChecksum { get; private set; }

    // Digs `amount` off the rock; true when this blow broke it — the cell is ground now, with the
    // rock's ore in it if it held any. Paths are rebuilt (Version).
    internal bool DamageRock(int x, int y, int amount)
    {
        int i = Index(x, y);
        int before = _rockHp[i];
        if (before <= 0 || amount <= 0)
            return false;
        int after = before > amount ? before - amount : 0;
        RockChecksum ^= Mix(i, before) ^ Mix(i, after);
        _rockHp[i] = after;
        if (after > 0)
            return false;

        TerrainStats rock = _terrain[i]!;
        _terrain[i] = null;
        _blocked[i] = false;
        if (rock.OreType != MapSetup.NoOre)
            AddOre(x, y, rock.OreType);
        Version++;
        return true;
    }

    private static ulong Mix(int cell, int hp)
    {
        ulong v = ((ulong)(uint)cell << 32) | (uint)hp;
        v ^= v >> 33;
        v *= 0xff51afd7ed558ccdUL;
        v ^= v >> 33;
        v *= 0xc4ceb9fe1a85ec53UL;
        return v ^ (v >> 33);
    }

    // terrain.md, decision 113: the side neighbour's deposit of the same resource with the lowest
    // id takes the cell; otherwise it starts a deposit of its own.
    private void AddOre(int x, int y, int type)
    {
        int i = Index(x, y);
        _ore[i] = type;
        int best = 0;
        for (int d = 0; d < 4; d++)
        {
            int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
            if (!InBounds(nx, ny))
                continue;
            int n = Index(nx, ny);
            if (_ore[n] == type && _depositAt[n] != 0 && (best == 0 || _depositAt[n] < best))
                best = _depositAt[n];
        }
        if (best != 0)
        {
            _depositAt[i] = best;
            _deposits[best - 1].Add(i);
            return;
        }
        int id = _deposits.Count + 1;
        _depositAt[i] = id;
        _deposits.Add(new Deposit(id, type, new List<int> { i }));
    }

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
        bool walkable = building.Stats.IsBelt;
        for (int y = building.CellY; y < building.CellY + building.Height; y++)
        {
            for (int x = building.CellX; x < building.CellX + building.Width; x++)
            {
                _buildingAt[Index(x, y)] = building.Id;
                _walkableBuilding[Index(x, y)] = walkable;
            }
        }
        // A belt changes nothing for walkers: cached paths stay good.
        if (!walkable)
            Version++;
    }

    internal void Free(Building building)
    {
        bool opened = false;
        for (int y = building.CellY; y < building.CellY + building.Height; y++)
        {
            for (int x = building.CellX; x < building.CellX + building.Width; x++)
            {
                if (_buildingAt[Index(x, y)] == building.Id)
                {
                    opened |= !_walkableBuilding[Index(x, y)];
                    _buildingAt[Index(x, y)] = 0;
                    _walkableBuilding[Index(x, y)] = false;
                }
            }
        }
        if (opened)
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

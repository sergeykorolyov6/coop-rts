using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// Fog of war shared by allies, as in SC2 Co-op (answer 3): a cell is visible to a player
// if any of their or their allies' units or buildings sees it. Discovered cells stay
// discovered for good; that is how a deposit goes Hidden → Discovered (drill.md).
public sealed class FogOfWar
{
    private readonly IReadOnlyList<Player> _players;
    private readonly int _width;
    private readonly int _height;
    private readonly MapState _map;

    // Scratch for Reveal: which cells of the square around the viewer the line of sight reaches.
    private bool[] _lit = new bool[0];
    private readonly List<LineTable?> _tables = new List<LineTable?>();
    private readonly bool[][] _ownSight;
    private readonly bool[][] _visible;
    private readonly bool[][] _discovered;

    // Players nobody plays against the fog for (the creeps, the neutral owner): their sight is
    // never used, so the update skips them — on a big map that is most of the fog's cost.
    private readonly bool[] _blind;

    internal FogOfWar(IReadOnlyList<Player> players, MapState map)
    {
        int width = map.Width, height = map.Height;
        _map = map;
        _players = players;
        _width = width;
        _height = height;
        _ownSight = new bool[players.Count][];
        _visible = new bool[players.Count][];
        _discovered = new bool[players.Count][];
        _blind = new bool[players.Count];
        for (int i = 0; i < players.Count; i++)
        {
            _ownSight[i] = new bool[width * height];
            _visible[i] = new bool[width * height];
            _discovered[i] = new bool[width * height];
        }
    }

    public bool IsVisible(int playerId, int x, int y) =>
        InBounds(x, y) && _visible[IndexOf(playerId)][y * _width + x];

    public bool IsDiscovered(int playerId, int x, int y) =>
        InBounds(x, y) && _discovered[IndexOf(playerId)][y * _width + x];

    // A deposit counts as discovered once any of its cells is.
    public bool IsDepositDiscovered(int playerId, Deposit deposit)
    {
        bool[] discovered = _discovered[IndexOf(playerId)];
        foreach (int cell in deposit.Cells)
        {
            if (discovered[cell])
                return true;
        }
        return false;
    }

    // Every discovered cell of a footprint (placement needs the whole site scouted).
    public bool IsAreaDiscovered(int playerId, int x, int y, int w, int h)
    {
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (!IsDiscovered(playerId, cx, cy))
                    return false;
            }
        }
        return true;
    }

    internal bool[] DiscoveredByIndex(int playerIndex) => _discovered[playerIndex];

    internal void SetBlind(int playerId) => _blind[IndexOf(playerId)] = true;

    // save-load.md, T5: what every player sees and has seen, a bit a cell. Sight is written too,
    // not worked out again: it is as of the last tick, and what a unit may strike depends on it.
    internal void WriteSnapshot(BinaryWriter w)
    {
        for (int i = 0; i < _players.Count; i++)
        {
            if (_blind[i])
                continue;
            WriteBits(w, _visible[i]);
            WriteBits(w, _discovered[i]);
        }
    }

    internal void ReadSnapshot(BinaryReader r)
    {
        for (int i = 0; i < _players.Count; i++)
        {
            if (_blind[i])
                continue;
            ReadBits(r, _visible[i]);
            ReadBits(r, _discovered[i]);
        }
    }

    private static void WriteBits(BinaryWriter w, bool[] cells)
    {
        for (int c = 0; c < cells.Length; c += 8)
        {
            byte bits = 0;
            for (int k = 0; k < 8 && c + k < cells.Length; k++)
            {
                if (cells[c + k])
                    bits |= (byte)(1 << k);
            }
            w.Write(bits);
        }
    }

    private static void ReadBits(BinaryReader r, bool[] cells)
    {
        for (int c = 0; c < cells.Length; c += 8)
        {
            byte bits = r.ReadByte();
            for (int k = 0; k < 8 && c + k < cells.Length; k++)
                cells[c + k] = (bits & (1 << k)) != 0;
        }
    }

    // A player without fog (creeps, the neutral owner): its units look with their own eyes (Sight).
    public bool IsBlind(int playerId) => _blind[IndexOf(playerId)];

    internal void Update(SimState state)
    {
        for (int i = 0; i < _players.Count; i++)
            System.Array.Clear(_ownSight[i], 0, _ownSight[i].Length);

        foreach (Unit u in state.Units)
        {
            if (!u.IsDead)
                Reveal(IndexOf(u.OwnerId), u.X, u.Y, u.Stats.VisionMilli, Sight.EyeLevel(_map, u));
        }
        foreach (Building b in state.Buildings)
        {
            if (b.IsDead)
                continue;
            int cx = Grid.CellToMilli(b.CellX) + Grid.CellToMilli(b.Width) / 2;
            int cy = Grid.CellToMilli(b.CellY) + Grid.CellToMilli(b.Height) / 2;
            // neutral-buildings.md: a lighthouse sees for whoever stands by it, never for itself.
            if (b.Stats.Behavior == BuildingBehavior.Lighthouse)
            {
                if (b.LitForPlayerId != 0)
                    Reveal(IndexOf(b.LitForPlayerId), cx, cy, b.Stats.VisionMilli, Sight.EyeLevel(_map, b));
                continue;
            }
            Reveal(IndexOf(b.OwnerId), cx, cy, b.Stats.VisionMilli, Sight.EyeLevel(_map, b));
        }

        // heights.md: whoever struck is seen by the victim's team for a while — its own cell only.
        foreach (Unit u in state.Units)
        {
            if (!u.IsDead && u.RevealedToPlayerId != 0 && state.Tick < u.RevealedUntilTick && InBounds(u.CellX, u.CellY))
                Show(u.RevealedToPlayerId, u.CellX, u.CellY, 1, 1);
        }
        foreach (Building b in state.Buildings)
        {
            if (!b.IsDead && b.RevealedToPlayerId != 0 && state.Tick < b.RevealedUntilTick)
                Show(b.RevealedToPlayerId, b.CellX, b.CellY, b.Width, b.Height);
        }

        for (int i = 0; i < _players.Count; i++)
        {
            if (_blind[i])
                continue;
            bool[] visible = _visible[i];
            bool[] discovered = _discovered[i];
            System.Array.Clear(visible, 0, visible.Length);
            for (int j = 0; j < _players.Count; j++)
            {
                if (_blind[j] || !state.Relations.IsAlly(_players[i].Id, _players[j].Id))
                    continue;
                bool[] sight = _ownSight[j];
                for (int c = 0; c < sight.Length; c++)
                {
                    if (sight[c])
                        visible[c] = true;
                }
            }
            for (int c = 0; c < visible.Length; c++)
            {
                if (visible[c])
                    discovered[c] = true;
            }
        }
    }

    private void Show(int playerId, int x, int y, int w, int h)
    {
        int index = IndexOf(playerId);
        if (_blind[index])
            return;
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
                _ownSight[index][cy * _width + cx] = true;
        }
    }

    // Cells whose centers lie within the radius of the point, are not above the eyes (heights.md) and
    // are not hidden behind rock (terrain.md): a cell is lit when the cell before it on the line from
    // the viewer's cell is lit and is not rock. The table walks the cells ring by ring, so that one
    // always comes first.
    private void Reveal(int playerIndex, int x, int y, int radius, int eyeLevel)
    {
        bool levels = _map.HasLevels;
        if (radius <= 0 || _blind[playerIndex])
            return;
        bool[] sight = _ownSight[playerIndex];
        long r2 = (long)radius * radius;
        int ox = Grid.MilliToCell(x), oy = Grid.MilliToCell(y);
        LineTable table = Table(radius / Grid.MilliPerCell + 1);
        if (_lit.Length < table.Size)
            _lit = new bool[table.Size];
        bool[] lit = _lit;
        int[] dxs = table.Dx, dys = table.Dy, parents = table.Parent;
        for (int k = 0; k < dxs.Length; k++)
        {
            int cx = ox + dxs[k], cy = oy + dys[k];
            bool on;
            if (!InBounds(cx, cy))
                on = false;
            else
            {
                int p = parents[k];
                on = p < 0 || (lit[p] && !_map.IsOpaque(ox + dxs[p], oy + dys[p]));
            }
            lit[k] = on;
            if (!on)
                continue;
            long ddx = Grid.CellCenter(cx) - x, ddy = Grid.CellCenter(cy) - y;
            if (ddx * ddx + ddy * ddy <= r2 && (!levels || _map.LevelAt(cx, cy) <= eyeLevel))
                sight[cy * _width + cx] = true;
        }
    }

    private LineTable Table(int reach)
    {
        while (_tables.Count <= reach)
            _tables.Add(null);
        return _tables[reach] ??= new LineTable(reach);
    }

    // The square of `reach` cells around a viewer, ring by ring; for each cell, the position of the
    // cell before it on the line of sight (-1: next to the viewer, always lit).
    private sealed class LineTable
    {
        public LineTable(int reach)
        {
            int side = 2 * reach + 1;
            Size = side * side;
            Dx = new int[Size];
            Dy = new int[Size];
            Parent = new int[Size];
            var position = new int[Size];
            int k = 0;
            for (int n = 0; n <= reach; n++)
            {
                for (int dy = -n; dy <= n; dy++)
                {
                    bool edgeRow = dy == -n || dy == n;
                    for (int dx = -n; dx <= n; dx += edgeRow || n == 0 ? 1 : 2 * n)
                    {
                        Dx[k] = dx;
                        Dy[k] = dy;
                        position[(dy + reach) * side + dx + reach] = k;
                        if (n <= 1)
                            Parent[k] = -1;
                        else
                        {
                            (int px, int py) = Sight.LineParent(dx, dy);
                            Parent[k] = position[(py + reach) * side + px + reach];
                        }
                        k++;
                    }
                }
            }
        }

        public int Size { get; }
        public int[] Dx { get; }
        public int[] Dy { get; }
        public int[] Parent { get; }
    }

    private bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < _width && y < _height;

    private int IndexOf(int playerId)
    {
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i].Id == playerId)
                return i;
        }
        throw new System.ArgumentException($"Unknown player {playerId}.", nameof(playerId));
    }
}

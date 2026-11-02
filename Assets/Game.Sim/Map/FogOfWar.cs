using System.Collections.Generic;

namespace Game.Sim;

// Fog of war shared by allies, as in SC2 Co-op (answer 3): a cell is visible to a player
// if any of their or their allies' units or buildings sees it. Discovered cells stay
// discovered for good; that is how a deposit goes Hidden → Discovered (drill.md).
public sealed class FogOfWar
{
    private readonly IReadOnlyList<Player> _players;
    private readonly int _width;
    private readonly int _height;
    private readonly bool[][] _ownSight;
    private readonly bool[][] _visible;
    private readonly bool[][] _discovered;

    internal FogOfWar(IReadOnlyList<Player> players, int width, int height)
    {
        _players = players;
        _width = width;
        _height = height;
        _ownSight = new bool[players.Count][];
        _visible = new bool[players.Count][];
        _discovered = new bool[players.Count][];
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

    internal void Update(SimState state)
    {
        for (int i = 0; i < _players.Count; i++)
            System.Array.Clear(_ownSight[i], 0, _ownSight[i].Length);

        foreach (Unit u in state.Units)
        {
            if (!u.IsDead)
                Reveal(IndexOf(u.OwnerId), u.X, u.Y, u.Stats.VisionMilli);
        }
        foreach (Building b in state.Buildings)
        {
            if (b.IsDead)
                continue;
            int cx = Grid.CellToMilli(b.CellX) + Grid.CellToMilli(b.Width) / 2;
            int cy = Grid.CellToMilli(b.CellY) + Grid.CellToMilli(b.Height) / 2;
            Reveal(IndexOf(b.OwnerId), cx, cy, b.Stats.VisionMilli);
        }

        for (int i = 0; i < _players.Count; i++)
        {
            bool[] visible = _visible[i];
            bool[] discovered = _discovered[i];
            System.Array.Clear(visible, 0, visible.Length);
            for (int j = 0; j < _players.Count; j++)
            {
                if (!state.Relations.IsAlly(_players[i].Id, _players[j].Id))
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

    // Cells whose centers lie within the radius of the point.
    private void Reveal(int playerIndex, int x, int y, int radius)
    {
        if (radius <= 0)
            return;
        bool[] sight = _ownSight[playerIndex];
        long r2 = (long)radius * radius;
        int minX = System.Math.Max(0, Grid.MilliToCell(x - radius)), maxX = System.Math.Min(_width - 1, Grid.MilliToCell(x + radius));
        int minY = System.Math.Max(0, Grid.MilliToCell(y - radius)), maxY = System.Math.Min(_height - 1, Grid.MilliToCell(y + radius));
        for (int cy = minY; cy <= maxY; cy++)
        {
            long dy = Grid.CellCenter(cy) - y;
            for (int cx = minX; cx <= maxX; cx++)
            {
                long dx = Grid.CellCenter(cx) - x;
                if (dx * dx + dy * dy <= r2)
                    sight[cy * _width + cx] = true;
            }
        }
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

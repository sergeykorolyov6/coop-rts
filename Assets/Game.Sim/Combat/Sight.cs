namespace Game.Sim;

// heights.md: who sees whom, and who stands "level-adjacent" to whom (no cliff between them).
internal static class Sight
{
    // The cell an entity stands on; a building's level is the same under all of it (belts are one cell).
    public static int StandLevel(MapState map, Entity e) =>
        e is Unit u ? (map.InBounds(u.CellX, u.CellY) ? map.LevelAt(u.CellX, u.CellY) : 0) : map.LevelAt(((Building)e).CellX, ((Building)e).CellY);

    public static int EyeLevel(MapState map, Entity e) =>
        e is Unit u ? (map.InBounds(u.CellX, u.CellY) ? map.EyeLevel(u.CellX, u.CellY) : 0) : map.EyeLevel(((Building)e).CellX, ((Building)e).CellY);

    // No cliff between the two: equal levels, or one stands on the ramp joining them.
    public static bool LevelAdjacent(MapState map, Entity a, Entity b)
    {
        (int ax, int ay) = Cell(a);
        (int bx, int by) = Cell(b);
        return !map.InBounds(ax, ay) || !map.InBounds(bx, by) || map.LevelsJoin(ax, ay, bx, by);
    }

    public static bool LevelAdjacent(MapState map, Unit u, int cellX, int cellY) =>
        !map.InBounds(u.CellX, u.CellY) || !map.InBounds(cellX, cellY) || map.LevelsJoin(u.CellX, u.CellY, cellX, cellY);

    // May the viewer strike or pick the target now: its team sees the target's cell (any cell of a
    // building). Players without fog (creeps, the neutral owner) look with their own eyes.
    public static bool Sees(SimState state, Entity viewer, Entity target)
    {
        if (!state.Fog.IsBlind(viewer.OwnerId))
            return IsVisibleTo(state, viewer.OwnerId, target);
        (int x, int y) = Cell(viewer);
        return SeesFrom(state, viewer.OwnerId, x, y, EyeLevel(state.Map, viewer), target);
    }

    // For a player without fog, looking from cell (fromX, fromY): not above the eyes and not behind
    // rock (terrain.md), or shown by the target's own blow at this player.
    public static bool SeesFrom(SimState state, int playerId, int fromX, int fromY, int eyeLevel, Entity target)
    {
        if (target.RevealedToPlayerId == playerId && state.Tick < target.RevealedUntilTick)
            return true;
        MapState map = state.Map;
        if (map.HasLevels && StandLevel(map, target) > eyeLevel)
            return false;
        if (target is Unit u)
            return LineOfSight(map, fromX, fromY, u.CellX, u.CellY);
        var b = (Building)target;
        for (int y = b.CellY; y < b.CellY + b.Height; y++)
        {
            for (int x = b.CellX; x < b.CellX + b.Width; x++)
            {
                if (LineOfSight(map, fromX, fromY, x, y))
                    return true;
            }
        }
        return false;
    }

    // terrain.md: the cell before (dx, dy) on the line from the viewer's cell — one Chebyshev step
    // nearer, offset × (n − 1) / n rounded half away from zero. Integers only, the same everywhere.
    public static (int X, int Y) LineParent(int dx, int dy)
    {
        int n = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
        return (RoundDiv(dx * (n - 1), n), RoundDiv(dy * (n - 1), n));
    }

    private static int RoundDiv(int a, int b) => a >= 0 ? (2 * a + b) / (2 * b) : -((-2 * a + b) / (2 * b));

    // No rock between the two cells (the end cells themselves may be rock): the same lines the fog uses.
    public static bool LineOfSight(MapState map, int fromX, int fromY, int toX, int toY)
    {
        int dx = toX - fromX, dy = toY - fromY;
        while (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) > 1)
        {
            (dx, dy) = LineParent(dx, dy);
            int x = fromX + dx, y = fromY + dy;
            if (!map.InBounds(x, y) || map.IsOpaque(x, y))
                return false;
        }
        return true;
    }

    public static bool IsVisibleTo(SimState state, int playerId, Entity target)
    {
        if (target is Unit u)
            return state.Map.InBounds(u.CellX, u.CellY) && state.Fog.IsVisible(playerId, u.CellX, u.CellY);
        var b = (Building)target;
        for (int y = b.CellY; y < b.CellY + b.Height; y++)
        {
            for (int x = b.CellX; x < b.CellX + b.Width; x++)
            {
                if (state.Fog.IsVisible(playerId, x, y))
                    return true;
            }
        }
        return false;
    }

    // heights.md: a ranged attacker standing higher than its target reaches farther.
    public static int HighGroundBonus(SimState state, Entity attacker, Entity target) =>
        state.Map.HasLevels && StandLevel(state.Map, attacker) > StandLevel(state.Map, target) ? state.Rules.HighGroundRangeBonusMilli : 0;

    private static (int X, int Y) Cell(Entity e) => e is Unit u ? (u.CellX, u.CellY) : (((Building)e).CellX, ((Building)e).CellY);
}

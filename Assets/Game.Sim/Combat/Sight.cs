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
    public static bool Sees(SimState state, Entity viewer, Entity target) =>
        state.Fog.IsBlind(viewer.OwnerId)
            ? SeesFromLevel(state, viewer.OwnerId, EyeLevel(state.Map, viewer), target)
            : IsVisibleTo(state, viewer.OwnerId, target);

    // For a player without fog: not above the eyes, or shown by its own blow at this player.
    public static bool SeesFromLevel(SimState state, int playerId, int eyeLevel, Entity target) =>
        !state.Map.HasLevels || StandLevel(state.Map, target) <= eyeLevel
        || (target.RevealedToPlayerId == playerId && state.Tick < target.RevealedUntilTick);

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

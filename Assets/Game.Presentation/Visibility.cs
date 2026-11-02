using Game.Sim;

namespace Game.Presentation
{
    // What the local player may see through the fog (shared by allies, FogOfWar). Every view
    // asks here, so the map, the minimap and picking never disagree.
    public static class Visibility
    {
        // Own and allied units always; others only on a cell seen right now.
        public static bool UnitShown(SimState state, int playerId, Unit unit) =>
            state.Relations.IsAlly(playerId, unit.OwnerId)
            || state.Fog.IsVisible(playerId, unit.CellX, unit.CellY);

        // Others' buildings stay on the map once their place was discovered, as in SC2
        // (last seen, not live; good enough until buildings in the fog get a snapshot).
        public static bool BuildingShown(SimState state, int playerId, Building building) =>
            state.Relations.IsAlly(playerId, building.OwnerId)
            || state.Fog.IsDiscovered(playerId, building.CellX, building.CellY);

        public static bool CellDiscovered(SimState state, int playerId, int x, int y) =>
            state.Fog.IsDiscovered(playerId, x, y);
    }
}

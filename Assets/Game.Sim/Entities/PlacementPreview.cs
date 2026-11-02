namespace Game.Sim;

// What the build menu shows before the click: the same answer BuildCommand gives for its first
// site, so a green silhouette never turns into a rejection (drill.md, conveyors.md UI).
public static class PlacementPreview
{
    public static RejectReason? Check(SimState state, int playerId, string buildingTypeId, int x, int y, Direction direction)
    {
        if (!state.Data.TryGetBuilding(buildingTypeId, out BuildingStats? stats))
            return RejectReason.NotAllowed;
        return Construction.CheckPlacement(state, playerId, stats!, x, y, direction)
            ?? Construction.CheckCanPay(state, playerId, stats!);
    }

    // Both bridge ends on one straight line, apart, within the bridge range (conveyors.md).
    public static bool InBridgeRange(SimState state, int x1, int y1, int x2, int y2) =>
        ConveyorSystem.InBridgeRange(state, x1, y1, x2, y2);
}

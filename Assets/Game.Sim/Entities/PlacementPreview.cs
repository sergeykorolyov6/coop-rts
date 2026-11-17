namespace Game.Sim;

// What the build menu shows before the click: the same answer BuildCommand gives for its first
// site, so a green silhouette never turns into a rejection (drill.md, conveyors.md UI).
// Money is told apart: a silhouette the wallet cannot pay for yet is placed and waits (builder-beam.md).
public static class PlacementPreview
{
    public static RejectReason? Check(SimState state, int playerId, string buildingTypeId, int x, int y, Direction direction)
    {
        if (!state.Data.TryGetBuilding(buildingTypeId, out BuildingStats? stats))
            return RejectReason.NotAllowed;
        return Construction.CheckPlacement(state, playerId, stats!, x, y, direction);
    }

    // How much of each resource the player still lacks for one such building; (0, 0) when it can pay now.
    public static (int Primary, int Secondary) Shortfall(SimState state, int playerId, string buildingTypeId)
    {
        if (!state.Data.TryGetBuilding(buildingTypeId, out BuildingStats? stats))
            return (0, 0);
        Wallet wallet = state.GetPlayer(playerId).Wallet;
        return (System.Math.Max(0, stats!.CostPrimary - wallet.Get(Resources.Primary)),
            System.Math.Max(0, stats.CostSecondary - wallet.Secondary));
    }

    // Both bridge ends on one straight line, apart, within the bridge range (conveyors.md).
    public static bool InBridgeRange(SimState state, int x1, int y1, int x2, int y2) =>
        ConveyorSystem.InBridgeRange(state, x1, y1, x2, y2);
}

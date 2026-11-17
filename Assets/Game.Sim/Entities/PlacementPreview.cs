using System.Collections.Generic;

namespace Game.Sim;

// What the UI asks the sim about building, read-only. Before the click: the same answer
// BuildCommand gives for its first site, so a green silhouette never turns into a rejection
// (drill.md, conveyors.md UI); money is told apart — a silhouette the wallet cannot pay for yet is
// placed and waits (builder-beam.md). About what stands: a belt's inputs, a stock's links, a
// builder's reach.
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

    // builder-beam.md: the unit builds or repairs this building from where it stands (the beam is
    // drawn then): within build_range of its edge and with no cliff between.
    public static bool InBuildRange(SimState state, Unit unit, Building building) =>
        Construction.InBuildRange(state, unit, building);

    // belt-merge.md: which sides of this belt own belts feed it from — its inputs on hover, and
    // the bars that join a side line to it.
    public static (bool Back, bool Left, bool Right) BeltInputs(SimState state, Building belt) =>
        ConveyorSystem.Inputs(state, belt);

    // factory-auto.md: the belts and bridge ends beside a building with a stock — those that feed it
    // and those its surplus goes on to, with the way the resource flows on each.
    public static void StockLinks(SimState state, Building building, List<(Building Link, Direction Flow, bool Feeds)> links) =>
        Logistics.StockLinks(state, building, links);

    // Both bridge ends on one straight line, apart, within the bridge range (conveyors.md).
    public static bool InBridgeRange(SimState state, int x1, int y1, int x2, int y2) =>
        ConveyorSystem.InBridgeRange(state, x1, y1, x2, y2);
}

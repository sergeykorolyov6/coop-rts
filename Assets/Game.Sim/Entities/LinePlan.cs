using System.Collections.Generic;

namespace Game.Sim;

// The result of LinePlanner (smart-line.md): the cells along the path in order, what they cost,
// and why the order stops early, if it does.
public sealed class LinePlan
{
    internal LinePlan(List<LineCell> cells, string buildingTypeId, string? bridgeTypeId, int costPrimary, int costSecondary,
        LineStop stop, int brokenSpan, bool mergesIntoBelt, int stuckToId)
    {
        Cells = cells;
        BuildingTypeId = buildingTypeId;
        BridgeTypeId = bridgeTypeId;
        CostPrimary = costPrimary;
        CostSecondary = costSecondary;
        Stop = stop;
        BrokenSpan = brokenSpan;
        MergesIntoBelt = mergesIntoBelt;
        StuckToId = stuckToId;
    }

    public IReadOnlyList<LineCell> Cells { get; }

    // What the Build and Relay cells order; bridge ends order BridgeTypeId (the faction's bridge).
    public string BuildingTypeId { get; }
    public string? BridgeTypeId { get; }

    // New cells and bridge ends; skips and relays are free.
    public int CostPrimary { get; }
    public int CostSecondary { get; }

    // Why the order stops before the cursor (the cells past the stop are Broken), None when it does not.
    public LineStop Stop { get; }

    public bool Blocked => Stop == LineStop.FirstCell;

    // With Stop == BridgeTooFar: how many cells the bridge would have to span.
    public int BrokenSpan { get; }

    // The last cell looks into an own belt and will feed it (belt-merge.md).
    public bool MergesIntoBelt { get; }

    // The building the last cell was turned to (a receiver beside the end), 0 for none.
    public int StuckToId { get; }

    public bool HasOrders
    {
        get
        {
            foreach (LineCell c in Cells)
            {
                if (c.Kind == LineCellKind.Build || c.Kind == LineCellKind.Relay || c.Kind == LineCellKind.BridgeIn)
                    return true;
            }
            return false;
        }
    }
}

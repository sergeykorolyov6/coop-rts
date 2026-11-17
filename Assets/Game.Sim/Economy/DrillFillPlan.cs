using System.Collections.Generic;

namespace Game.Sim;

// The result of DrillFill (drill-fill.md): drills in build order, and the cells with ore that got none.
public sealed class DrillFillPlan
{
    internal DrillFillPlan(List<DrillFillCell> sites, List<DrillFillCell> skipped, int oreTotal)
    {
        Sites = sites;
        Skipped = skipped;
        OreTotal = oreTotal;
    }

    // Richest first; equal ones bottom-up, then left to right. At most BuildCommand.MaxSites.
    public IReadOnlyList<DrillFillCell> Sites { get; }

    public IReadOnlyList<DrillFillCell> Skipped { get; }

    // Ore cells under all the drills.
    public int OreTotal { get; }
}

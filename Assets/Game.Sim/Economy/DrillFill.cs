using System.Collections.Generic;

namespace Game.Sim;

// drill-fill.md (U9): drills over a frame as a grid of their own size. Of the grid's shifts against
// the frame's corner the one with most ore under its drills wins (then fewer drills, then the
// smaller vertical, then horizontal shift). A drill goes where it is whole inside the frame, the
// place is buildable and ore covers at least drill_fill_min_ore_pct of it. Only reads the state.
public static class DrillFill
{
    public static DrillFillPlan Plan(SimState state, int playerId, string drillTypeId, int x0, int y0, int x1, int y1)
    {
        var none = new DrillFillPlan(new List<DrillFillCell>(), new List<DrillFillCell>(), 0);
        if (!state.Data.TryGetBuilding(drillTypeId, out BuildingStats? stats) || stats!.Behavior != BuildingBehavior.Drill)
            return none;
        MapState map = state.Map;
        int minX = System.Math.Max(0, System.Math.Min(x0, x1)), maxX = System.Math.Min(map.Width - 1, System.Math.Max(x0, x1));
        int minY = System.Math.Max(0, System.Math.Min(y0, y1)), maxY = System.Math.Min(map.Height - 1, System.Math.Max(y0, y1));
        int w = stats.SizeW, h = stats.SizeH;
        if (maxX - minX + 1 < w || maxY - minY + 1 < h)
            return none;

        List<DrillFillCell>? best = null, bestSkipped = null;
        int bestOre = -1;
        for (int dy = 0; dy < h; dy++)
        {
            for (int dx = 0; dx < w; dx++)
            {
                var sites = new List<DrillFillCell>();
                var skipped = new List<DrillFillCell>();
                int ore = 0;
                for (int y = minY + dy; y + h - 1 <= maxY; y += h)
                {
                    for (int x = minX + dx; x + w - 1 <= maxX; x += w)
                    {
                        int count = DrillPreview.Count(map, x, y, w, h, out int resource);
                        if (count == 0)
                            continue;
                        DrillFillCellKind kind = (long)count * 100 < (long)state.Rules.DrillFillMinOrePercent * w * h ? DrillFillCellKind.Poor
                            : Construction.CheckPlacement(state, playerId, stats, x, y, Direction.Right) != null ? DrillFillCellKind.Taken
                            : DrillFillCellKind.Placed;
                        var cell = new DrillFillCell(x, y, count, resource, kind);
                        if (kind == DrillFillCellKind.Placed)
                        {
                            sites.Add(cell);
                            ore += count;
                        }
                        else
                        {
                            skipped.Add(cell);
                        }
                    }
                }
                // Shifts come in order of dy, then dx: a later one wins only by more ore or fewer drills.
                if (best == null || ore > bestOre || (ore == bestOre && sites.Count < best.Count))
                {
                    best = sites;
                    bestSkipped = skipped;
                    bestOre = ore;
                }
            }
        }

        best!.Sort(Richest);
        if (best.Count > BuildCommand.MaxSites)
        {
            for (int i = BuildCommand.MaxSites; i < best.Count; i++)
                bestOre -= best[i].Ore;
            best.RemoveRange(BuildCommand.MaxSites, best.Count - BuildCommand.MaxSites);
        }
        return new DrillFillPlan(best, bestSkipped!, bestOre);
    }

    private static int Richest(DrillFillCell a, DrillFillCell b)
    {
        if (a.Ore != b.Ore)
            return b.Ore.CompareTo(a.Ore);
        return a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);
    }
}

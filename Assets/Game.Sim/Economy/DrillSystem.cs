using System.Collections.Generic;

namespace Game.Sim;

internal static class DrillSystem
{
    private static readonly List<Building> Neighbours = new List<Building>();

    // Drills in id order: when two of them offer to the same receiver in one tick, the lower id
    // goes first (conveyors.md, edge cases).
    public static void Tick(SimState state)
    {
        foreach (Building b in state.Buildings)
        {
            Drill? drill = b.Drill;
            if (drill == null || b.IsDead || !b.IsComplete)
                continue;

            Mine(state, drill);
            if (drill.Buffer > 0)
                Output(state, b, drill);
            drill.IsStopped = drill.Buffer >= state.Rules.DrillBuffer;
        }
    }

    private static void Mine(SimState state, Drill drill)
    {
        if (drill.Buffer >= state.Rules.DrillBuffer || drill.OreCells == 0)
            return;
        drill.Accumulator += drill.OreCells;
        if (drill.Accumulator >= state.Rules.DrillTicksPerUnitPerOreCell)
        {
            drill.Accumulator -= state.Rules.DrillTicksPerUnitPerOreCell;
            drill.Buffer++;
        }
    }

    // One unit per tick to the next neighbour (round-robin) that takes it.
    private static void Output(SimState state, Building b, Drill drill)
    {
        Logistics.Neighbours(state, b, Neighbours);
        int n = Neighbours.Count;
        for (int k = 0; k < n; k++)
        {
            int index = (drill.OutputCursor + k) % n;
            if (Logistics.TryGiveFromBuilding(state, b, Neighbours[index], drill.ResourceType))
            {
                drill.Buffer--;
                drill.OutputCursor = (index + 1) % n;
                return;
            }
        }
    }
}

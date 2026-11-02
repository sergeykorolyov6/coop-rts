using System.Collections.Generic;

namespace Game.Sim;

// Drills mine and hand out; storages hand out. In id order (conveyors.md, appendix).
internal static class DrillSystem
{
    private static readonly List<Building> Neighbours = new List<Building>();

    // Drills in id order: when two of them offer to the same receiver in one tick, the lower id
    // goes first (conveyors.md, edge cases).
    public static void Tick(SimState state)
    {
        foreach (Building b in state.Buildings)
        {
            if (b.IsDead || !b.IsComplete)
                continue;

            Drill? drill = b.Drill;
            if (drill != null)
            {
                Mine(state, drill);
                if (drill.Buffer > 0)
                    Output(state, b, drill);
                drill.IsStopped = drill.Buffer >= state.Rules.DrillBuffer;
            }
            else if (b.Storage != null && b.Storage.Total > 0)
            {
                OutputStorage(state, b, b.Storage);
            }
        }
    }

    // Like a drill, one unit per tick round-robin; the resource it holds more of goes first.
    private static void OutputStorage(SimState state, Building b, Storage storage)
    {
        int type = storage.Secondary > storage.Primary ? Resources.Secondary : Resources.Primary;
        Logistics.Neighbours(state, b, Neighbours);
        int n = Neighbours.Count;
        for (int k = 0; k < n; k++)
        {
            int index = (storage.OutputCursor + k) % n;
            if (Logistics.TryGive(state, b, Logistics.Giver.Storage, Neighbours[index], type))
            {
                if (type == Resources.Secondary)
                    storage.Secondary--;
                else
                    storage.Primary--;
                storage.OutputCursor = (index + 1) % n;
                return;
            }
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
            if (Logistics.TryGive(state, b, Logistics.Giver.Drill, Neighbours[index], drill.ResourceType))
            {
                drill.Buffer--;
                drill.OutputCursor = (index + 1) % n;
                return;
            }
        }
    }
}

using System.Collections.Generic;

namespace Game.Sim;

internal static class FactorySystem
{
    public static void Tick(SimState state, List<SimEvent> events)
    {
        // Index loop: spawned units go to the unit list, not this one, but stay safe anyway.
        IReadOnlyList<Building> buildings = state.Buildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            Building b = buildings[i];
            Factory? factory = b.Factory;
            if (factory == null || b.IsDead || !b.IsComplete || factory._queue.Count == 0)
                continue;

            UnitStats stats = state.Data.Unit(factory._queue[0]);
            if (factory.Progress < stats.BuildTimeTicks)
                factory.Progress++;
            if (factory.Progress < stats.BuildTimeTicks)
                continue;

            // Ready: stand on the nearest free cell; with none free it waits (factory.md, fail cases).
            List<(int X, int Y)> cells = state.FreeCellsAround(b.CellX, b.CellY, b.Width, b.Height, 1);
            if (cells.Count == 0)
                continue;
            Unit unit = state.SpawnUnit(b.OwnerId, stats, Grid.CellCenter(cells[0].X), Grid.CellCenter(cells[0].Y));
            if (factory.HasRally)
                unit.Order = UnitOrder.MoveTo(factory.RallyX, factory.RallyY);
            factory._queue.RemoveAt(0);
            factory.Progress = 0;
            events.Add(new UnitProduced(state.Tick, b.OwnerId, b.Id, unit.Id));
        }
    }
}

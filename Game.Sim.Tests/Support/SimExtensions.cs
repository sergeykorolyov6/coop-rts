using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Sim.Tests;

internal static class SimExtensions
{
    public static void Run(this Simulation sim, int ticks, Action<Simulation>? eachTick = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            sim.Tick();
            eachTick?.Invoke(sim);
        }
    }

    // Runs until the condition holds; returns the number of ticks taken. Fails after maxTicks.
    public static int RunUntil(this Simulation sim, Func<Simulation, bool> condition, int maxTicks = 20 * 60 * 5)
    {
        for (int i = 1; i <= maxTicks; i++)
        {
            sim.Tick();
            if (condition(sim))
                return i;
        }
        throw new InvalidOperationException($"Condition not met in {maxTicks} ticks.");
    }

    // Collects events of a type over the run, since Events only holds the last tick.
    public static List<T> RunCollecting<T>(this Simulation sim, int ticks) where T : SimEvent
    {
        var result = new List<T>();
        sim.Run(ticks, s => result.AddRange(s.Events.OfType<T>()));
        return result;
    }

    public static List<Unit> UnitsOf(this SimState state, int playerId) =>
        state.Units.Where(u => u.OwnerId == playerId).ToList();

    public static int Cells(int cells) => cells * Grid.MilliPerCell;

    // A unit standing in the centre of cell (cx, cy).
    public static Unit Spawn(Simulation sim, int owner, string type, int cx, int cy) =>
        sim.State.SpawnUnit(owner, sim.State.Data.Unit(type), Center(cx), Center(cy));

    // Dead now; removed at the end of the next tick, as after a killing blow.
    public static void Kill(Entity e)
    {
        e.Hp = 0;
        e.IsDead = true;
    }

    public static int Center(int cell) => Grid.CellCenter(cell);

    public static void Order(this Simulation sim, ICommand command) => sim.Enqueue(command);

    public static MoveCommand Move(this Simulation sim, int playerId, int x, int y, params Unit[] units) =>
        new MoveCommand(playerId, sim.State.Tick, units.Select(u => u.Id).ToArray(), x, y);
}

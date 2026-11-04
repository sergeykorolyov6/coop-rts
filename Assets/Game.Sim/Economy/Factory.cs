using System.Collections.Generic;

namespace Game.Sim;

// factory.md: a queue of paid-for units, made one at a time; M2: a rally point. The stock it pays
// from, when belts supply it, is the building's (Building.Stock).
public sealed class Factory
{
    internal readonly List<string> _queue = new List<string>();


    // Unit type ids, the one in production first.
    public IReadOnlyList<string> Queue => _queue;

    // Ticks spent on the first unit of the queue.
    public int Progress { get; internal set; }

    // Where finished units walk to, milli-cells.
    public bool HasRally { get; internal set; }
    public int RallyX { get; internal set; }
    public int RallyY { get; internal set; }
}

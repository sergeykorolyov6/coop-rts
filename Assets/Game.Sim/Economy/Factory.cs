using System.Collections.Generic;

namespace Game.Sim;

// factory.md: a queue of paid-for units, made one at a time; M2: a rally point. The stock it pays
// from, when belts supply it, is the building's (Building.Stock).
public sealed class Factory
{
    internal readonly List<string> _queue = new List<string>();

    // factory-auto.md: per place of the queue, whether it is paid; a repeat goes in unpaid and
    // is paid from the stock when it reaches the front.
    internal readonly List<bool> _paid = new List<bool>();

    public IReadOnlyList<bool> Paid => _paid;

    // factory-auto.md: a finished unit goes back to the end of the queue, unpaid.
    public bool Repeat { get; internal set; }

    // Unit type ids, the one in production first.
    public IReadOnlyList<string> Queue => _queue;

    // Ticks spent on the first unit of the queue.
    public int Progress { get; internal set; }

    // Where finished units walk to, milli-cells.
    public bool HasRally { get; internal set; }
    public int RallyX { get; internal set; }
    public int RallyY { get; internal set; }
}

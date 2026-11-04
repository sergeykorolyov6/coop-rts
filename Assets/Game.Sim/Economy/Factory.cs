using System.Collections.Generic;

namespace Game.Sim;

// factory.md: a queue of paid-for units, made one at a time; M2: a rally point and, for buildings
// supplied by conveyor, an own stock that belts fill and production is paid from.
public sealed class Factory
{
    internal readonly List<string> _queue = new List<string>();

    internal Factory(Wallet? stock)
    {
        Stock = stock;
    }

    // Unit type ids, the one in production first.
    public IReadOnlyList<string> Queue => _queue;

    // Ticks spent on the first unit of the queue.
    public int Progress { get; internal set; }

    // factory.md M2: null when production is paid from the owner's wallet (the Legion's factory).
    public Wallet? Stock { get; }

    // Where finished units walk to, milli-cells.
    public bool HasRally { get; internal set; }
    public int RallyX { get; internal set; }
    public int RallyY { get; internal set; }
}

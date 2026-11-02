using System.Collections.Generic;

namespace Game.Sim;

// factory.md: a queue of paid-for units, made one at a time.
public sealed class Factory
{
    internal readonly List<string> _queue = new List<string>();

    // Unit type ids, the one in production first.
    public IReadOnlyList<string> Queue => _queue;

    // Ticks spent on the first unit of the queue.
    public int Progress { get; internal set; }
}

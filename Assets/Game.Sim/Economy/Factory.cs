using System.Collections.Generic;

namespace Game.Sim;

// factory.md: a queue of paid-for units, made one at a time; M2: a rally point. The stock it pays
// from, when belts supply it, is the building's (Building.Stock).
public sealed class Factory
{
    private readonly List<string> _queue = new List<string>();
    private readonly List<bool> _paid = new List<bool>();

    // Unit type ids, the one in production first.
    public IReadOnlyList<string> Queue => _queue;

    // factory-auto.md: per place of the queue, whether it is paid for. A unit the player queues is
    // paid at once; a repeat goes in unpaid and is paid from the stock when it reaches the front.
    public IReadOnlyList<bool> Paid => _paid;

    // factory-auto.md: a finished unit goes back to the end of the queue, unpaid.
    public bool Repeat { get; internal set; }

    // Ticks spent on the first unit of the queue.
    public int Progress { get; internal set; }

    internal void Enqueue(string unitId, bool paid)
    {
        _queue.Add(unitId);
        _paid.Add(paid);
    }

    // The first place takes the progress with it.
    internal void RemoveAt(int index)
    {
        _queue.RemoveAt(index);
        _paid.RemoveAt(index);
        if (index == 0)
            Progress = 0;
    }

    internal void MarkPaid(int index) => _paid[index] = true;

    // Where finished units walk to, milli-cells.
    public bool HasRally { get; internal set; }
    public int RallyX { get; internal set; }
    public int RallyY { get; internal set; }
}

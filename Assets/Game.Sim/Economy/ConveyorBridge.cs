using System.Collections.Generic;

namespace Game.Sim;

// One end of a conveyor bridge (conveyors.md, "Мост"). The input end takes resources like a
// conveyor and carries them over whatever lies between to the linked output end.
public sealed class ConveyorBridge
{
    internal readonly List<BridgeItem> _items = new List<BridgeItem>();

    // Output end this one sends to (set on the input end), 0 when unlinked.
    public int LinkedToId { get; internal set; }

    // Input end sending here (set on the output end), 0 when none.
    public int LinkedFromId { get; internal set; }

    // Once an end has been an output it stays one, so a rebuilt input links to it the right way round.
    public bool IsOutputEnd { get; internal set; }

    public int ItemsInFlight => _items.Count;
}

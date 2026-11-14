using System.Collections.Generic;

namespace Game.Sim;

// One base of the Black Legion as the map draws it (legion-ai.md, T5): its buildings in rebuild
// order and where its army gathers. Each base is played by its own Legion player.
public sealed class LegionBaseSetup
{
    internal readonly List<BaseSlotSetup> _slots = new List<BaseSlotSetup>();

    public IReadOnlyList<BaseSlotSetup> Slots => _slots;

    public bool HasRally { get; internal set; }
    public int RallyX { get; internal set; }
    public int RallyY { get; internal set; }
}

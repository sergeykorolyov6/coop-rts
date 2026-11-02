namespace Game.Sim;

// A building of the Legion's base and its rebuild state (legion-ai.md).
public sealed class BaseSlot
{
    internal BaseSlot(BaseSlotSetup setup)
    {
        Setup = setup;
    }

    public BaseSlotSetup Setup { get; }

    // The building standing (or being built) in the slot, 0 when there is none.
    public int BuildingId { get; internal set; }

    // When the slot lost its building; Entity.NeverTick while it stands.
    public int DestroyedTick { get; internal set; } = Entity.NeverTick;

    // Legion worker sent to rebuild it, 0 when none.
    public int WorkerId { get; internal set; }
}

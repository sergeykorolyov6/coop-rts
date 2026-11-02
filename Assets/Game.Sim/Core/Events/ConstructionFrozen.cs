namespace Game.Sim;

// drill.md: no builder worked on the site this tick; it waits for any own or ally worker.
public sealed class ConstructionFrozen : SimEvent
{
    public ConstructionFrozen(int tick, int ownerId, int buildingId)
        : base(tick)
    {
        OwnerId = ownerId;
        BuildingId = buildingId;
    }

    public int OwnerId { get; }
    public int BuildingId { get; }
}

namespace Game.Sim;

// The site is finished and works from now on.
public sealed class ConstructionCompleted : SimEvent
{
    public ConstructionCompleted(int tick, int ownerId, int buildingId)
        : base(tick)
    {
        OwnerId = ownerId;
        BuildingId = buildingId;
    }

    public int OwnerId { get; }
    public int BuildingId { get; }
}

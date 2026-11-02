namespace Game.Sim;

public sealed class EntityDestroyed : SimEvent
{
    public EntityDestroyed(int tick, int ownerId, int entityId, bool isBuilding)
        : base(tick)
    {
        OwnerId = ownerId;
        EntityId = entityId;
        IsBuilding = isBuilding;
    }

    public int OwnerId { get; }
    public int EntityId { get; }
    public bool IsBuilding { get; }
}

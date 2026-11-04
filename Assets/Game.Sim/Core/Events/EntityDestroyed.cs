namespace Game.Sim;

public sealed class EntityDestroyed : SimEvent
{
    public EntityDestroyed(int tick, int ownerId, int entityId, bool isBuilding, bool demolished = false)
        : base(tick)
    {
        OwnerId = ownerId;
        EntityId = entityId;
        IsBuilding = isBuilding;
        Demolished = demolished;
    }

    public int OwnerId { get; }
    public int EntityId { get; }
    public bool IsBuilding { get; }

    // demolish.md: the owner took it down; RefundPrimary/Secondary went back to the wallet.
    public bool Demolished { get; }
    public int RefundPrimary { get; internal set; }
    public int RefundSecondary { get; internal set; }
}

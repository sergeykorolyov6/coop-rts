namespace Game.Sim;

public sealed class EntityDestroyed : SimEvent
{
    public EntityDestroyed(int tick, int ownerId, int entityId, bool isBuilding, bool demolished = false, string typeId = "")
        : base(tick)
    {
        TypeId = typeId;
        OwnerId = ownerId;
        EntityId = entityId;
        IsBuilding = isBuilding;
        Demolished = demolished;
    }

    public int OwnerId { get; }
    public int EntityId { get; }
    public bool IsBuilding { get; }

    // The unit or building type: the entity is gone from the state by the time the UI hears of it.
    public string TypeId { get; }

    // demolish.md: the owner took it down; RefundPrimary/Secondary went back to the wallet.
    public bool Demolished { get; }
    public int RefundPrimary { get; internal set; }
    public int RefundSecondary { get; internal set; }
}

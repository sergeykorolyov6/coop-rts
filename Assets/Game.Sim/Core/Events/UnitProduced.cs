namespace Game.Sim;

public sealed class UnitProduced : SimEvent
{
    public UnitProduced(int tick, int ownerId, int factoryId, int unitId)
        : base(tick)
    {
        OwnerId = ownerId;
        FactoryId = factoryId;
        UnitId = unitId;
    }

    public int OwnerId { get; }
    public int FactoryId { get; }
    public int UnitId { get; }
}

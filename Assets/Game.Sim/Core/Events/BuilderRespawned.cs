namespace Game.Sim;

// builder.md: the builder stands again next to a headquarters.
public sealed class BuilderRespawned : SimEvent
{
    public BuilderRespawned(int tick, int playerId, int unitId, int headquartersId)
        : base(tick)
    {
        PlayerId = playerId;
        UnitId = unitId;
        HeadquartersId = headquartersId;
    }

    public int PlayerId { get; }
    public int UnitId { get; }
    public int HeadquartersId { get; }
}

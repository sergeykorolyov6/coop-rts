namespace Game.Sim;

// factions.md: the hero stands again next to a headquarters.
public sealed class HeroRespawned : SimEvent
{
    public HeroRespawned(int tick, int playerId, int unitId)
        : base(tick)
    {
        PlayerId = playerId;
        UnitId = unitId;
    }

    public int PlayerId { get; }
    public int UnitId { get; }
}

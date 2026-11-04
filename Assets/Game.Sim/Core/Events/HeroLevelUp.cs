namespace Game.Sim;

// factions.md: the player's hero reached a new level.
public sealed class HeroLevelUp : SimEvent
{
    public HeroLevelUp(int tick, int playerId, int unitId, int level)
        : base(tick)
    {
        PlayerId = playerId;
        UnitId = unitId;
        Level = level;
    }

    public int PlayerId { get; }
    public int UnitId { get; }
    public int Level { get; }
}

namespace Game.Sim;

// factions.md: an ability went off — for the effect on screen. X, Y: where (the point, or the user).
public sealed class AbilityUsed : SimEvent
{
    public AbilityUsed(int tick, int playerId, int unitId, string abilityId, int x, int y)
        : base(tick)
    {
        PlayerId = playerId;
        UnitId = unitId;
        X = x;
        Y = y;
        AbilityId = abilityId;
    }

    public int PlayerId { get; }
    public int UnitId { get; }
    public int X { get; }
    public int Y { get; }
    public string AbilityId { get; }
}

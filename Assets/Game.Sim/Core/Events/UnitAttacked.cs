namespace Game.Sim;

// alerts.md: alarm for the owner and allies; at most once per attack_alarm_cooldown per unit.
public sealed class UnitAttacked : SimEvent
{
    public UnitAttacked(int tick, int ownerId, int unitId, int attackerId)
        : base(tick)
    {
        OwnerId = ownerId;
        UnitId = unitId;
        AttackerId = attackerId;
    }

    public int OwnerId { get; }
    public int UnitId { get; }
    public int AttackerId { get; }
}

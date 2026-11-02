namespace Game.Sim;

// drill.md / combat.md: alarm and minimap mark for the owner and allies; at most once per
// attack_alarm_cooldown per building.
public sealed class BuildingAttacked : SimEvent
{
    public BuildingAttacked(int tick, int ownerId, int buildingId, int attackerId)
        : base(tick)
    {
        OwnerId = ownerId;
        BuildingId = buildingId;
        AttackerId = attackerId;
    }

    public int OwnerId { get; }
    public int BuildingId { get; }
    public int AttackerId { get; }
}

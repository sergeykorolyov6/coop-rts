namespace Game.Sim;

// combat.md UI: a hit was applied — the view flashes the target and draws the strike.
public sealed class HitLanded : SimEvent
{
    public HitLanded(int tick, int attackerId, int targetId, int damage)
        : base(tick)
    {
        AttackerId = attackerId;
        TargetId = targetId;
        Damage = damage;
    }

    public int AttackerId { get; }
    public int TargetId { get; }
    public int Damage { get; }
}

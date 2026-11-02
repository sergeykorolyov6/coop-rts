namespace Game.Sim;

// A blow struck this tick; all of them land together after every unit has acted.
internal readonly struct PendingHit
{
    public PendingHit(int attackerId, int targetId, int amount)
    {
        AttackerId = attackerId;
        TargetId = targetId;
        Amount = amount;
    }

    public int AttackerId { get; }
    public int TargetId { get; }
    public int Amount { get; }
}

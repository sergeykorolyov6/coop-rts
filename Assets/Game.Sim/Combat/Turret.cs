namespace Game.Sim;

// turret.md: a building that strikes by itself. State of its gun.
public sealed class Turret
{
    // Ticks until the next shot is allowed.
    public int Cooldown { get; internal set; }

    // Entity it is shooting at, 0 when none.
    public int TargetId { get; internal set; }
}

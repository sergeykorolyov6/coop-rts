namespace Game.Sim;

// builder.md: the player's builder is dead; it comes back at RespawnTick near a headquarters.
public sealed class BuilderDied : SimEvent
{
    public BuilderDied(int tick, int playerId, int respawnTick)
        : base(tick)
    {
        PlayerId = playerId;
        RespawnTick = respawnTick;
    }

    public int PlayerId { get; }
    public int RespawnTick { get; }
}

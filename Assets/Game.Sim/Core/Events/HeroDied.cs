namespace Game.Sim;

// factions.md: the player's hero is dead; it comes back at RespawnTick with its level.
public sealed class HeroDied : SimEvent
{
    public HeroDied(int tick, int playerId, int respawnTick)
        : base(tick)
    {
        PlayerId = playerId;
        RespawnTick = respawnTick;
    }

    public int PlayerId { get; }
    public int RespawnTick { get; }
}

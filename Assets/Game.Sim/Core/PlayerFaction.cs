namespace Game.Sim;

// A player's faction chosen for the match (the lobby, a mission); players not listed get the
// first faction of their role (factions.csv).
public sealed class PlayerFaction
{
    public PlayerFaction(int playerId, string factionId)
    {
        PlayerId = playerId;
        FactionId = factionId;
    }

    public int PlayerId { get; }
    public string FactionId { get; }
}

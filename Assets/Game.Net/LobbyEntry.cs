namespace Game.Net;

// A taken place in the lobby, as the host tells it.
public sealed class LobbyEntry
{
    public LobbyEntry(int slot, string name, string factionId)
    {
        Slot = slot;
        Name = name;
        FactionId = factionId;
    }

    public int Slot { get; }
    public string Name { get; }
    public string FactionId { get; }
}

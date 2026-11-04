namespace Game.Sim;

// neutral-buildings.md: a lighthouse started shining for the player (and so for the allies).
public sealed class LighthouseLit : SimEvent
{
    public LighthouseLit(int tick, int playerId, int buildingId)
        : base(tick)
    {
        PlayerId = playerId;
        BuildingId = buildingId;
    }

    public int PlayerId { get; }
    public int BuildingId { get; }
}

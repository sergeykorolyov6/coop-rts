namespace Game.Sim;

// neutral-buildings.md: a neutral building now belongs to the player.
public sealed class BuildingCaptured : SimEvent
{
    public BuildingCaptured(int tick, int playerId, int buildingId)
        : base(tick)
    {
        PlayerId = playerId;
        BuildingId = buildingId;
    }

    public int PlayerId { get; }
    public int BuildingId { get; }
}

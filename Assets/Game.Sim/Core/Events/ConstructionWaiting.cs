namespace Game.Sim;

// builder-beam.md: a worker reached a silhouette its player cannot pay for yet and waits by it;
// sent once when the waiting starts, not every tick.
public sealed class ConstructionWaiting : SimEvent
{
    public ConstructionWaiting(int tick, int playerId, int unitId, string buildingTypeId, int cellX, int cellY)
        : base(tick)
    {
        PlayerId = playerId;
        UnitId = unitId;
        BuildingTypeId = buildingTypeId;
        CellX = cellX;
        CellY = cellY;
    }

    public int PlayerId { get; }
    public int UnitId { get; }
    public string BuildingTypeId { get; }
    public int CellX { get; }
    public int CellY { get; }
}

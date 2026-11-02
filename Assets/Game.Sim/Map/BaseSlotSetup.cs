namespace Game.Sim;

// One building of the Legion's base as the map draws it (legion-ai.md): built at the start,
// rebuilt on the same spot when destroyed.
public sealed class BaseSlotSetup
{
    internal BaseSlotSetup(string buildingTypeId, int cellX, int cellY, Direction direction)
    {
        BuildingTypeId = buildingTypeId;
        CellX = cellX;
        CellY = cellY;
        Direction = direction;
    }

    public string BuildingTypeId { get; }
    public int CellX { get; }
    public int CellY { get; }
    public Direction Direction { get; }
}

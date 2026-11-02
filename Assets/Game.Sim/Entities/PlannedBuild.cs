namespace Game.Sim;

// A silhouette the worker walks to (state "Запланирован"); becomes a site once started.
public sealed class PlannedBuild
{
    internal PlannedBuild(string buildingTypeId, int cellX, int cellY, Direction direction)
    {
        BuildingTypeId = buildingTypeId;
        CellX = cellX;
        CellY = cellY;
        Direction = direction;
    }

    public string BuildingTypeId { get; }
    public int CellX { get; }
    public int CellY { get; }

    // Facing for conveyors and bridges; ignored by other buildings.
    public Direction Direction { get; }

    // Site to help build instead of a new one (right-click on a frozen or ally site): its id; 0 otherwise.
    public int ExistingSiteId { get; internal set; }
}

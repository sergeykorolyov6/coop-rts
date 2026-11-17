namespace Game.Sim;

// rebuild-ghosts.md: the trace a destroyed building leaves — its kind, cell, direction and owner.
// Not a building: takes no cell, no damage, gives no sight.
public sealed class Ghost
{
    internal Ghost(int id, int ownerId, string buildingTypeId, int cellX, int cellY, Direction direction, int destroyedTick)
    {
        Id = id;
        OwnerId = ownerId;
        BuildingTypeId = buildingTypeId;
        CellX = cellX;
        CellY = cellY;
        Direction = direction;
        DestroyedTick = destroyedTick;
    }

    public int Id { get; }
    public int OwnerId { get; }
    public string BuildingTypeId { get; }
    public int CellX { get; }
    public int CellY { get; }
    public Direction Direction { get; }
    public int DestroyedTick { get; }
}

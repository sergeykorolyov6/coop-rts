namespace Game.Sim;

// One cell of a build order: the bottom-left cell of the footprint and the facing.
public readonly struct BuildSite
{
    public BuildSite(int cellX, int cellY, Direction direction)
    {
        CellX = cellX;
        CellY = cellY;
        Direction = direction;
    }

    public int CellX { get; }
    public int CellY { get; }
    public Direction Direction { get; }
}

namespace Game.Sim;

// Where a player's headquarters stands at the start (bottom-left cell of its footprint).
public readonly struct PlayerStart
{
    public PlayerStart(int playerId, int cellX, int cellY)
    {
        PlayerId = playerId;
        CellX = cellX;
        CellY = cellY;
    }

    public int PlayerId { get; }
    public int CellX { get; }
    public int CellY { get; }
}

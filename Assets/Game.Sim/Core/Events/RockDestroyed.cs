namespace Game.Sim;

// terrain.md: a rock cell was dug out — it is ground now, or ore if the rock held some.
public sealed class RockDestroyed : SimEvent
{
    public RockDestroyed(int tick, int playerId, int minerId, int cellX, int cellY, string terrainId)
        : base(tick)
    {
        PlayerId = playerId;
        MinerId = minerId;
        CellX = cellX;
        CellY = cellY;
        TerrainId = terrainId;
    }

    public int PlayerId { get; }
    public int MinerId { get; }
    public int CellX { get; }
    public int CellY { get; }
    public string TerrainId { get; }
}

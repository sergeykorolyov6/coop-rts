namespace Game.Sim;

// manual-mining.md: the ore cell was taken while the worker dug; it hands in what it has and stops.
public sealed class MiningStopped : SimEvent
{
    public MiningStopped(int tick, int playerId, int workerId, int cellX, int cellY)
        : base(tick)
    {
        PlayerId = playerId;
        WorkerId = workerId;
        CellX = cellX;
        CellY = cellY;
    }

    public int PlayerId { get; }
    public int WorkerId { get; }
    public int CellX { get; }
    public int CellY { get; }
}

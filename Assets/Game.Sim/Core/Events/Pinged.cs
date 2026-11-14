namespace Game.Sim;

// coop-tools.md: a team player marked a point on the map for the allies.
public sealed class Pinged : SimEvent
{
    public Pinged(int tick, int playerId, int x, int y)
        : base(tick)
    {
        PlayerId = playerId;
        X = x;
        Y = y;
    }

    public int PlayerId { get; }

    // Milli-cells.
    public int X { get; }
    public int Y { get; }
}

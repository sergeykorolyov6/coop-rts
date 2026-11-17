namespace Game.Sim;

// One conveyor cell (conveyors.md). With one cell of spacing between units a cell never
// holds more than one, so the "queue on a segment" is one slot per cell with an integer
// position; the chain is walked head to tail each tick (ConveyorSystem).
public sealed class Conveyor
{
    public const int Empty = -1;

    // Milli-cells from the cell's back edge to its front edge.
    public const int CellLength = Grid.MilliPerCell;

    // Resource type of the unit on this cell, or Empty.
    public int ItemType { get; internal set; } = Empty;

    // How far the unit has travelled across the cell, 0..CellLength.
    public int ItemPos { get; internal set; }

    public bool HasItem => ItemType != Empty;

    // belt-merge.md: which input entered last — they take turns round the circle back → left → right.
    // Sides are relative to this cell's own direction.
    public const int FromNobody = 0, FromBack = 1, FromLeft = 2, FromRight = 3;

    public int LastFeeder { get; internal set; }

    internal void Clear()
    {
        ItemType = Empty;
        ItemPos = 0;
    }
}

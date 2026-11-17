namespace Game.Sim;

// One grid cell of a drill frame (drill-fill.md): its bottom-left cell, the ore under it
// (of the resource it has most of) and what becomes of it.
public readonly struct DrillFillCell
{
    public DrillFillCell(int x, int y, int ore, int resourceType, DrillFillCellKind kind)
    {
        X = x;
        Y = y;
        Ore = ore;
        ResourceType = resourceType;
        Kind = kind;
    }

    public int X { get; }
    public int Y { get; }
    public int Ore { get; }
    public int ResourceType { get; }
    public DrillFillCellKind Kind { get; }
}

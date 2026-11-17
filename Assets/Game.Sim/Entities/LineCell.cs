namespace Game.Sim;

// One cell of a planned line (smart-line.md): where, which way, and what becomes of it.
public readonly struct LineCell
{
    public LineCell(int x, int y, Direction direction, LineCellKind kind)
    {
        X = x;
        Y = y;
        Direction = direction;
        Kind = kind;
    }

    public int X { get; }
    public int Y { get; }
    public Direction Direction { get; }
    public LineCellKind Kind { get; }

    internal LineCell With(LineCellKind kind) => new LineCell(X, Y, Direction, kind);

    internal LineCell With(Direction direction, LineCellKind kind) => new LineCell(X, Y, direction, kind);
}

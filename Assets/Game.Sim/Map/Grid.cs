using System;

namespace Game.Sim;

// Map dimensions in cells. World positions are integer milli-cells: cell (x, y) covers
// [x * MilliPerCell, (x + 1) * MilliPerCell) on each axis.
public sealed class Grid
{
    public const int MilliPerCell = 1000;

    public Grid(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "Must be positive.");

        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    public bool InBounds(int cellX, int cellY) =>
        cellX >= 0 && cellY >= 0 && cellX < Width && cellY < Height;

    public static int CellToMilli(int cell) => cell * MilliPerCell;

    public static int MilliToCell(int milli) => IntMath.FloorDiv(milli, MilliPerCell);

    public static int CellCenter(int cell) => cell * MilliPerCell + MilliPerCell / 2;
}

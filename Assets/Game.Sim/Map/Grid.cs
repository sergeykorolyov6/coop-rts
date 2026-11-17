namespace Game.Sim;

// World positions are integer milli-cells: cell (x, y) covers
// [x * MilliPerCell, (x + 1) * MilliPerCell) on each axis. The size of the map is MapState's.
public static class Grid
{
    public const int MilliPerCell = 1000;

    public static int CellToMilli(int cell) => cell * MilliPerCell;

    public static int MilliToCell(int milli) => IntMath.FloorDiv(milli, MilliPerCell);

    public static int CellCenter(int cell) => cell * MilliPerCell + MilliPerCell / 2;

    // A footprint (x, y, w, h) shares a cell with the frame of cells minX..maxX, minY..maxY.
    public static bool Touches(int x, int y, int w, int h, int minX, int minY, int maxX, int maxY) =>
        x <= maxX && x + w - 1 >= minX && y <= maxY && y + h - 1 >= minY;
}

namespace Game.Sim;

// drill-fill.md: a grid cell of the frame — a drill goes there, or why not.
public enum DrillFillCellKind : byte
{
    Placed,

    // Ore under it, but less than drill_fill_min_ore_pct of its cells.
    Poor,

    // Ore under it, but the place is not buildable (taken, undiscovered, uneven…).
    Taken,
}

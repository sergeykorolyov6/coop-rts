namespace Game.Sim;

// smart-line.md: what becomes of one cell of a dragged line.
public enum LineCellKind : byte
{
    // A new belt (or wall) is ordered here.
    Build,

    // Already there: an own belt looking the same way; for a wall line, a cell it cannot stand on.
    Skip,

    // An own belt turned to the line's direction, free (relay-and-demolish.md).
    Relay,

    BridgeIn,
    BridgeOut,

    // Under a bridge's span: nothing is ordered here.
    UnderBridge,

    // The order stops before this cell (no bridge reaches): shown red, nothing ordered.
    Broken,
}

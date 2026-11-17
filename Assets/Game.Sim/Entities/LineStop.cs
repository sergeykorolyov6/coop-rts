namespace Game.Sim;

// smart-line.md: why a planned line does not reach the cursor.
public enum LineStop : byte
{
    // It does: every cell is ordered, skipped or bridged over.
    None,

    // The first cell is an obstacle: nothing can be ordered.
    FirstCell,

    // What is in the way is longer than a bridge reaches (LinePlan.BrokenSpan cells).
    BridgeTooFar,

    // A bridge would reach, but cannot stand: a bend in the span or right after it, an existing
    // belt or another bridge end where its end would go, or the faction builds no bridge.
    NoBridge,
}

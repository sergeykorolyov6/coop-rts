namespace Game.Sim;

// drill.md: mines the ore under it into a buffer and hands it to neighbouring buildings.
public sealed class Drill
{
    internal Drill(int resourceType, int oreCells)
    {
        ResourceType = resourceType;
        OreCells = oreCells;
    }

    // Resource it mines: the one with most ore cells under it (ties: the lower type).
    public int ResourceType { get; }

    // Ore cells of that resource under the footprint: speed is proportional to it.
    public int OreCells { get; }

    public int Buffer { get; internal set; }

    // "Остановлен": the buffer is full and nothing takes from it.
    public bool IsStopped { get; internal set; }

    // Each tick adds OreCells; every DrillTicksPerUnitPerOreCell gives one unit (drill.md, appendix).
    internal int Accumulator { get; set; }

    // Round-robin position among the neighbours that accept resources.
    internal int OutputCursor { get; set; }
}

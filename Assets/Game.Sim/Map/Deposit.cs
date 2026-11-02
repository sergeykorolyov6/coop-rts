using System.Collections.Generic;

namespace Game.Sim;

// A connected patch of ore cells of one resource type (drill.md: "месторождение").
public sealed class Deposit
{
    private readonly List<int> _cells;

    internal Deposit(int id, int resourceType, List<int> cells)
    {
        Id = id;
        ResourceType = resourceType;
        _cells = cells;
    }

    public int Id { get; }
    public int ResourceType { get; }

    // Cell indices (y * width + x), ascending.
    public IReadOnlyList<int> Cells => _cells;
}

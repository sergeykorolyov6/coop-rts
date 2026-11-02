using System.Collections.Generic;

namespace Game.Sim;

public sealed class CreepCampSetup
{
    private readonly List<(int X, int Y)> _creepCells;

    internal CreepCampSetup(string campTypeId, int centerX, int centerY, IEnumerable<(int X, int Y)> creepCells)
    {
        CampTypeId = campTypeId;
        CenterX = centerX;
        CenterY = centerY;
        _creepCells = new List<(int X, int Y)>(creepCells);
    }

    public string CampTypeId { get; }
    public int CenterX { get; }
    public int CenterY { get; }

    // Home cell of each creep, matching CreepCampStats.Creeps by index.
    public IReadOnlyList<(int X, int Y)> CreepCells => _creepCells;
}

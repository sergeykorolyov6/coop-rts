namespace Game.Sim;

// neutral-buildings.md: a neutral building on the map before the match.
public sealed class NeutralBuildingSetup
{
    public NeutralBuildingSetup(string buildingTypeId, int cellX, int cellY, int guardCampIndex)
    {
        BuildingTypeId = buildingTypeId;
        CellX = cellX;
        CellY = cellY;
        GuardCampIndex = guardCampIndex;
    }

    public string BuildingTypeId { get; }
    public int CellX { get; }
    public int CellY { get; }

    // Index of the guarding camp in MapSetup.CreepCamps, -1 for none.
    public int GuardCampIndex { get; }
}

namespace Game.Sim;

// Building ids from buildings.csv that have their own behaviour in the sim.
public static class BuildingKinds
{
    public const string Headquarters = "headquarters";
    public const string Factory = "factory";
    public const string Drill = "drill";
    public const string Conveyor = "conveyor";
    public const string ConveyorBridge = "conveyor_bridge";
    public const string Storage = "storage";

    // Ground units walk over conveyors and bridge ends (conveyors.md, question 27).
    public static bool IsWalkable(string id) => id == Conveyor || id == ConveyorBridge;
}

namespace Game.Sim;

// Gives a building the behaviour of its kind (drill, conveyor, storage, factory) when placed.
internal static class BuildingComponents
{
    public static void Attach(SimState state, Building building)
    {
        switch (building.Stats.Id)
        {
            case BuildingKinds.Drill:
                int cells = DrillPreview.Count(state.Map, building.CellX, building.CellY, building.Width, building.Height, out int type);
                building.Drill = new Drill(type, cells);
                break;
            case BuildingKinds.Conveyor:
                building.Conveyor = new Conveyor();
                break;
            case BuildingKinds.ConveyorBridge:
                building.Bridge = new ConveyorBridge();
                ConveyorSystem.LinkNewBridgeEnd(state, building);
                break;
            case BuildingKinds.Storage:
                building.Storage = new Storage();
                break;
            case BuildingKinds.Factory:
                building.Factory = new Factory();
                break;
        }
    }

    // Before a destroyed building leaves the state. Resources on it are lost (answer 14).
    public static void Detach(SimState state, Building building)
    {
        if (building.Bridge != null)
            ConveyorSystem.Unlink(state, building);
    }
}

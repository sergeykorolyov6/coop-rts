namespace Game.Sim;

// Gives a building the behaviour of its kind (drill, conveyor, storage, factory) when placed.
internal static class BuildingComponents
{
    public static void Attach(SimState state, Building building)
    {
        switch (building.Stats.Behavior)
        {
            case BuildingBehavior.Drill:
                int cells = DrillPreview.Count(state.Map, building.CellX, building.CellY, building.Width, building.Height, out int type);
                building.Drill = new Drill(type, cells);
                break;
            case BuildingBehavior.Conveyor:
                building.Conveyor = new Conveyor();
                break;
            case BuildingBehavior.Bridge:
                building.Bridge = new ConveyorBridge();
                ConveyorSystem.LinkNewBridgeEnd(state, building);
                break;
            case BuildingBehavior.Storage:
                building.Storage = new Storage();
                break;
        }
        // Whatever produces units (the players' factory, the Legion's own) has a queue (factory.md).
        if (building.Stats.Produces.Count > 0)
            building.Factory = new Factory(building.Stats.SuppliedByConveyor ? new Wallet(state.ResourceTypeCount) : null);
        // Any building with a gun shoots by itself (turret.md), whoever owns it.
        if (building.Stats.Damage > 0)
            building.Turret = new Turret();
    }

    // Before a destroyed building leaves the state. Resources on it are lost (answer 14).
    public static void Detach(SimState state, Building building)
    {
        if (building.Bridge != null)
            ConveyorSystem.Unlink(state, building);
    }
}

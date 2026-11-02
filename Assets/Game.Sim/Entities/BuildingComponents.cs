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
        }
    }
}

namespace Game.Presentation
{
    // Building ids of buildings.csv that have their own model or menu hotkey. Art and input only:
    // gameplay reacts to BuildingStats.Behavior, never to these ids.
    public static class BuildingIds
    {
        public const string Headquarters = "headquarters";
        public const string Factory = "factory";
        public const string LegionFactory = "legion_factory";
        public const string Drill = "drill";
        public const string Conveyor = "conveyor";
        public const string ConveyorBridge = "conveyor_bridge";
        public const string Storage = "storage";
        public const string Turret = "turret";
        public const string Cave = "cave";
        public const string Lighthouse = "lighthouse";
    }
}

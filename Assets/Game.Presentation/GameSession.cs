using Game.Sim;

namespace Game.Presentation
{
    // What the next load of the game scene should start: a mission from the menu, or a save to replay
    // (save-load.md). Static, so it survives SceneManager.LoadScene; empty means "show the main menu".
    public static class GameSession
    {
        public static string? MissionId { get; private set; }
        public static int Seed { get; private set; } = 1;
        public static SaveGame? PendingSave { get; private set; }

        // Shown once by the main menu (a save that could not be loaded, and why).
        public static string? Error { get; set; }

        public static void StartMission(string missionId, int seed)
        {
            MissionId = missionId;
            Seed = seed;
            PendingSave = null;
        }

        public static void Load(SaveGame save)
        {
            MissionId = save.MissionId;
            Seed = save.Seed;
            PendingSave = save;
        }

        // The save has been replayed: a later scene reload starts the mission afresh, not the save.
        public static void SaveConsumed() => PendingSave = null;

        public static void ToMenu()
        {
            MissionId = null;
            PendingSave = null;
        }
    }
}

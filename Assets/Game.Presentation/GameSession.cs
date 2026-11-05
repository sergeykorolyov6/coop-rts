using System.Collections.Generic;
using Game.Net;
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

        // network.md: factions chosen in the lobby, one per start slot; null — the mission's own.
        public static IReadOnlyList<string>? Factions { get; private set; }

        // The network game this machine takes part in (lobby or match); null for a local game.
        public static NetSession? Net { get; set; }

        // save-load.md: the recorded match to watch; null when playing.
        public static SaveGame? Replay { get; private set; }

        // A network match started by the host: every machine reloads into the same mission.
        public static bool IsNetworkMatch => Net?.Client?.Start != null && MissionId != null;

        // Shown once by the main menu (a save that could not be loaded, and why).
        public static string? Error { get; set; }

        public static void StartMission(string missionId, int seed)
        {
            MissionId = missionId;
            Seed = seed;
            PendingSave = null;
            Factions = null;
            Replay = null;
        }

        // A save picked by the host is restored on every machine (network.md, save-load.md).
        public static void StartNetworkMatch(MatchStart start)
        {
            MissionId = start.MissionId;
            Seed = start.Seed;
            PendingSave = start.Save != null ? SaveGame.Read(start.Save) : null;
            Factions = start.Factions;
            Replay = null;
        }

        public static void StartReplay(SaveGame replay)
        {
            MissionId = replay.MissionId;
            Seed = replay.Seed;
            PendingSave = null;
            Factions = replay.FactionsOrNull;
            Replay = replay;
        }

        public static void Load(SaveGame save)
        {
            Factions = save.FactionsOrNull;
            Replay = null;
            MissionId = save.MissionId;
            Seed = save.Seed;
            PendingSave = save;
        }

        // The save has been replayed: a later scene reload starts the mission afresh, not the save.
        public static void SaveConsumed() => PendingSave = null;

        // Leaves a network game too: the menu is never part of one.
        public static void ToMenu()
        {
            MissionId = null;
            PendingSave = null;
            Factions = null;
            Replay = null;
            LeaveNetwork();
        }

        public static void LeaveNetwork()
        {
            Net?.Close();
            Net = null;
        }
    }
}

using System.Collections.Generic;

namespace Game.Net;

// What every machine starts: the same mission, seed and factions (MissionLauncher.CreateSetup),
// from the beginning or from the same save (save-load.md: SaveGame.Restore up to StartTick).
public sealed class MatchStart
{
    public MatchStart(string missionId, int seed, IReadOnlyList<string> factions, int startTick = 0, byte[]? save = null)
    {
        MissionId = missionId;
        Seed = seed;
        Factions = factions;
        StartTick = startTick;
        Save = save;
    }

    public string MissionId { get; }
    public int Seed { get; }

    // Faction of slot 1, 2, …
    public IReadOnlyList<string> Factions { get; }

    // 0 for a new match; a save's tick when the match goes on from it.
    public int StartTick { get; }

    // SaveGame.Write bytes, or null for a new match.
    public byte[]? Save { get; }
}

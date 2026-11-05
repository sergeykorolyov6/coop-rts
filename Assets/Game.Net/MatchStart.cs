using System.Collections.Generic;

namespace Game.Net;

// What every machine starts: the same mission, seed and factions (MissionLauncher.CreateSetup).
public sealed class MatchStart
{
    public MatchStart(string missionId, int seed, IReadOnlyList<string> factions)
    {
        MissionId = missionId;
        Seed = seed;
        Factions = factions;
    }

    public string MissionId { get; }
    public int Seed { get; }

    // Faction of slot 1, 2, …
    public IReadOnlyList<string> Factions { get; }
}

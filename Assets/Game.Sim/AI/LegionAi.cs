using System.Collections.Generic;

namespace Game.Sim;

// The Black Legion's mind (legion-ai.md). Everything it remembers between ticks is here and
// in the state hash; the decisions are in LegionAiSystem.
public sealed class LegionAi
{
    internal readonly List<BaseSlot> _slots = new List<BaseSlot>();
    internal readonly List<Wave> _waves = new List<Wave>();

    internal LegionAi(int playerId, LegionStats stats, int rallyX, int rallyY)
    {
        PlayerId = playerId;
        Stats = stats;
        RallyX = rallyX;
        RallyY = rallyY;
    }

    public int PlayerId { get; }

    // The legion.csv row for the number of players in the match.
    public LegionStats Stats { get; }

    // Cell where waiting units gather and waves leave from.
    public int RallyX { get; }
    public int RallyY { get; }

    public IReadOnlyList<BaseSlot> Slots => _slots;

    // Waves on their way, oldest first.
    public IReadOnlyList<Wave> Waves => _waves;

    // Number of the next wave (1-based) and the player it will go for, chosen at the warning.
    public int NextWave { get; internal set; } = 1;
    public int NextWaveTargetPlayerId { get; internal set; }
    public bool NextWaveWarned { get; internal set; }

    // Base building the waiting units defend, 0 when the base is calm.
    public int DefendBuildingId { get; internal set; }
}

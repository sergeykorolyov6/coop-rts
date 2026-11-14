using System.Collections.Generic;

namespace Game.Sim;

// The mind of one Black Legion base (legion-ai.md; a big map has several, each with its own
// player). Everything it remembers between ticks is here and in the state hash; the decisions
// are in LegionAiSystem.
public sealed class LegionAi
{
    internal readonly List<BaseSlot> _slots = new List<BaseSlot>();
    internal readonly List<Wave> _waves = new List<Wave>();
    private readonly List<int> _targetPlayerIds;

    internal LegionAi(int playerId, LegionStats stats, int rallyX, int rallyY, List<int> targetPlayerIds)
    {
        PlayerId = playerId;
        Stats = stats;
        RallyX = rallyX;
        RallyY = rallyY;
        _targetPlayerIds = targetPlayerIds;
    }

    public int PlayerId { get; }

    // The legion.csv row for the number of players this base plays against.
    public LegionStats Stats { get; }

    // Cell where waiting units gather and waves leave from.
    public int RallyX { get; }
    public int RallyY { get; }

    // The team players this base sends its waves to, ascending: dealt to the bases in turn at the
    // start, so it follows from the match's players and is not hashed.
    public IReadOnlyList<int> TargetPlayerIds => _targetPlayerIds;

    // A base nobody was dealt to (one player on a map with two bases): it defends and rebuilds,
    // but sends no waves.
    public bool IsDormant => _targetPlayerIds.Count == 0;

    public IReadOnlyList<BaseSlot> Slots => _slots;

    // Waves on their way, oldest first.
    public IReadOnlyList<Wave> Waves => _waves;

    // Number of the next wave (1-based) and the player it will go for, chosen at the warning.
    public int NextWave { get; internal set; } = 1;
    public int NextWaveTargetPlayerId { get; internal set; }
    public bool NextWaveWarned { get; internal set; }

    // When the next wave leaves (for the HUD timer); follows from NextWave, so not hashed.
    // int.MaxValue for a dormant base.
    public int NextWaveTick { get; internal set; } = int.MaxValue;

    // When the warned wave should reach its target: the launch plus the way from the rally point
    // at the pace of its slowest unit. int.MaxValue until the warning.
    public int NextWaveArrivalTick { get; internal set; } = int.MaxValue;

    // Base building the waiting units defend, 0 when the base is calm.
    public int DefendBuildingId { get; internal set; }
}

namespace Game.Sim;

// legion-ai.md: "волна N идёт к игроку X", ai_wave_warning before it leaves.
public sealed class WaveIncoming : SimEvent
{
    public WaveIncoming(int tick, int legionPlayerId, int wave, int targetPlayerId, int launchTick, int arrivalTick)
        : base(tick)
    {
        LegionPlayerId = legionPlayerId;
        Wave = wave;
        TargetPlayerId = targetPlayerId;
        LaunchTick = launchTick;
        ArrivalTick = arrivalTick;
    }

    // The base the wave leaves from (T5: a map may have several).
    public int LegionPlayerId { get; }

    public int Wave { get; }
    public int TargetPlayerId { get; }
    public int LaunchTick { get; }

    // T5: when it should reach its target — the launch plus the way at the pace of its slowest
    // unit; equal to LaunchTick when there is no way to estimate.
    public int ArrivalTick { get; }
}

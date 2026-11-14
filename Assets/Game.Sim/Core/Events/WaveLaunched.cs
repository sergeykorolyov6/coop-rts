namespace Game.Sim;

// legion-ai.md: a wave left the Legion's rally point. Not raised when nobody was ready.
public sealed class WaveLaunched : SimEvent
{
    public WaveLaunched(int tick, int legionPlayerId, int wave, int targetPlayerId, int targetId, int unitCount, int arrivalTick)
        : base(tick)
    {
        LegionPlayerId = legionPlayerId;
        Wave = wave;
        TargetPlayerId = targetPlayerId;
        TargetId = targetId;
        UnitCount = unitCount;
        ArrivalTick = arrivalTick;
    }

    // The base the wave left (T5).
    public int LegionPlayerId { get; }

    // T5: when it should reach the target, by the way and its slowest unit; Tick when unknown.
    public int ArrivalTick { get; }

    public int Wave { get; }
    public int TargetPlayerId { get; }

    // Entity the wave goes for first.
    public int TargetId { get; }

    public int UnitCount { get; }
}

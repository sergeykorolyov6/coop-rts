namespace Game.Sim;

// legion-ai.md: a wave left the Legion's rally point. Not raised when nobody was ready.
public sealed class WaveLaunched : SimEvent
{
    public WaveLaunched(int tick, int wave, int targetPlayerId, int targetId, int unitCount)
        : base(tick)
    {
        Wave = wave;
        TargetPlayerId = targetPlayerId;
        TargetId = targetId;
        UnitCount = unitCount;
    }

    public int Wave { get; }
    public int TargetPlayerId { get; }

    // Entity the wave goes for first.
    public int TargetId { get; }

    public int UnitCount { get; }
}

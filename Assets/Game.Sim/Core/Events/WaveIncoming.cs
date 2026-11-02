namespace Game.Sim;

// legion-ai.md: "волна N идёт к игроку X", ai_wave_warning before it leaves.
public sealed class WaveIncoming : SimEvent
{
    public WaveIncoming(int tick, int wave, int targetPlayerId, int launchTick)
        : base(tick)
    {
        Wave = wave;
        TargetPlayerId = targetPlayerId;
        LaunchTick = launchTick;
    }

    public int Wave { get; }
    public int TargetPlayerId { get; }
    public int LaunchTick { get; }
}

namespace Game.Sim;

// mission.md: the team won or lost; comes once.
public sealed class MissionEnded : SimEvent
{
    public MissionEnded(int tick, MissionOutcome outcome)
        : base(tick)
    {
        Outcome = outcome;
    }

    public MissionOutcome Outcome { get; }
}

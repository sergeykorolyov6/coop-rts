namespace Game.Sim.Tests;

internal sealed class MarkEvent : SimEvent
{
    public MarkEvent(int tick, int playerId, string label)
        : base(tick)
    {
        PlayerId = playerId;
        Label = label;
    }

    public int PlayerId { get; }
    public string Label { get; }

    public override string ToString() => $"{Tick}:{PlayerId}:{Label}";
}

namespace Game.Sim;

// A calm creep camp woke up (creep-camp.md).
public sealed class CampAlerted : SimEvent
{
    public CampAlerted(int tick, int campIndex)
        : base(tick)
    {
        CampIndex = campIndex;
    }

    public int CampIndex { get; }
}

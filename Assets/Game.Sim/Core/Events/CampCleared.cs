namespace Game.Sim;

// The last creep of a camp died: "лагерь зачищен" for every player, the trophy appears.
public sealed class CampCleared : SimEvent
{
    public CampCleared(int tick, int campIndex)
        : base(tick)
    {
        CampIndex = campIndex;
    }

    public int CampIndex { get; }
}

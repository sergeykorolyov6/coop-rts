namespace Game.Sim;

// "legion_gunner:3" in waves.csv.
public readonly struct WaveUnit
{
    public WaveUnit(string unitId, int count)
    {
        UnitId = unitId;
        Count = count;
    }

    public string UnitId { get; }
    public int Count { get; }
}

namespace Game.Sim;

public sealed class Building : Entity
{
    private readonly int _startHp;

    internal Building(int id, int ownerId, BuildingStats stats, int cellX, int cellY, Direction direction, bool complete, int startHpPercent)
        : base(id, ownerId, stats.Hp, stats.Armor)
    {
        Stats = stats;
        CellX = cellX;
        CellY = cellY;
        Direction = direction;
        BuildWork = stats.BuildTimeTicks;

        if (complete || BuildWork == 0)
        {
            State = BuildState.Complete;
            BuildProgress = BuildWork;
        }
        else
        {
            State = BuildState.Constructing;
            _startHp = System.Math.Max(1, stats.Hp * startHpPercent / 100);
            Hp = _startHp;
        }
    }

    public BuildingStats Stats { get; }

    // Bottom-left cell of the footprint.
    public int CellX { get; }
    public int CellY { get; }
    public int Width => Stats.SizeW;
    public int Height => Stats.SizeH;
    public Direction Direction { get; }

    public BuildState State { get; internal set; }
    public bool IsComplete => State == BuildState.Complete;

    // Work done and needed; each builder adds 1 per tick.
    public int BuildProgress { get; private set; }
    public int BuildWork { get; }

    // Builders that worked on the site in the last tick (for the progress bar).
    public int BuildersLastTick { get; internal set; }

    public bool OccupiesCell(int x, int y) =>
        x >= CellX && y >= CellY && x < CellX + Width && y < CellY + Height;

    // Adds work and the matching share of HP: HP grows linearly from the start value to
    // MaxHp over the build, so damage taken meanwhile stays subtracted (as in SC2).
    internal void AddWork(int work)
    {
        int before = BuildProgress;
        BuildProgress = System.Math.Min(BuildWork, BuildProgress + work);
        Hp = System.Math.Min(MaxHp, Hp + HpAt(BuildProgress) - HpAt(before));
        if (BuildProgress == BuildWork)
            State = BuildState.Complete;
    }

    private int HpAt(int progress) =>
        _startHp + (int)((long)(MaxHp - _startHp) * progress / BuildWork);
}

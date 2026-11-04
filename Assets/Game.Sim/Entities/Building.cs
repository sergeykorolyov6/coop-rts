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

    public Drill? Drill { get; internal set; }
    public Factory? Factory { get; internal set; }

    // factory.md M2, factions.md: the own stock of a building supplied by conveyor (a factory pays
    // for units from it, an ammo turret for shots); null for the rest.
    public Wallet? Stock { get; internal set; }

    public Conveyor? Conveyor { get; internal set; }
    public ConveyorBridge? Bridge { get; internal set; }
    public Storage? Storage { get; internal set; }
    public Turret? Turret { get; internal set; }

    // demolish.md: ticks until the building is gone, 0 when it is not being demolished.
    public int DemolishTicksLeft { get; internal set; }

    // neutral-buildings.md: capture of a neutral building (ticks and who is capturing, 0 for nobody),
    // the guard camp that must be cleared first (-1 for none), and who a lighthouse shines for (0: dark).
    public int CaptureProgress { get; internal set; }
    public int CapturePlayerId { get; internal set; }
    public int GuardCampIndex { get; internal set; } = -1;
    public int LitForPlayerId { get; internal set; }

    // factions.md: building self-repair (Blue Walls), exact HP accumulator.
    internal int RegenAccumulator { get; set; }

    // Builders or repairers counted so far in the current tick, against MaxBuilders.
    internal int WorkersThisTick { get; set; }

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

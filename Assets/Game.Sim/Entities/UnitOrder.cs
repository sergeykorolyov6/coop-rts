namespace Game.Sim;

// What the player last told the unit to do. Immutable: a new order replaces the old one.
// Which fields matter depends on Kind: a point for Move, a cell for Mine,
// an entity id for Attack/Repair/Build, a camp index for CarryTrophy.
public sealed class UnitOrder
{
    public static readonly UnitOrder Idle = new UnitOrder(UnitOrderKind.Idle, 0, 0, 0);

    private UnitOrder(UnitOrderKind kind, int x, int y, int targetId)
    {
        Kind = kind;
        X = x;
        Y = y;
        TargetId = targetId;
    }

    public UnitOrderKind Kind { get; }
    public int X { get; }
    public int Y { get; }
    public int TargetId { get; }

    public static UnitOrder MoveTo(int x, int y) => new UnitOrder(UnitOrderKind.Move, x, y, 0);
    public static UnitOrder Attack(int targetId) => new UnitOrder(UnitOrderKind.Attack, 0, 0, targetId);
    public static UnitOrder Mine(int cellX, int cellY) => new UnitOrder(UnitOrderKind.Mine, cellX, cellY, 0);
    public static UnitOrder Build() => new UnitOrder(UnitOrderKind.Build, 0, 0, 0);
    public static UnitOrder Repair(int buildingId) => new UnitOrder(UnitOrderKind.Repair, 0, 0, buildingId);
    public static UnitOrder ReturnCargo() => new UnitOrder(UnitOrderKind.ReturnCargo, 0, 0, 0);
    public static UnitOrder AttackMoveTo(int x, int y) => new UnitOrder(UnitOrderKind.AttackMove, x, y, 0);
    public static UnitOrder CarryTrophy(int campIndex) => new UnitOrder(UnitOrderKind.CarryTrophy, 0, 0, campIndex);
}

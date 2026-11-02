namespace Game.Sim;

public enum UnitOrderKind : byte
{
    Idle,
    Move,
    Attack,
    Mine,
    Build,
    Repair,
    CarryTrophy,
    ReturnCargo,

    // legion-ai.md: walk to a point, fighting whatever enemy comes into sight on the way.
    AttackMove,
}

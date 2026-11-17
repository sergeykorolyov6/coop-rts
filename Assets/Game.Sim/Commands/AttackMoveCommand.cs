using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// attack-move.md (U8): right-click on the ground — fighters walk there fighting every enemy they
// see on the way (UnitOrderKind.AttackMove, as the Legion's waves go); builders, scouts and units
// that cannot attack just walk.
public sealed class AttackMoveCommand : ICommand
{
    public const int Id = 17;

    private readonly int[] _unitIds;

    public AttackMoveCommand(int playerId, int tick, int[] unitIds, int x, int y, bool queued = false)
    {
        PlayerId = playerId;
        Tick = tick;
        _unitIds = (int[])unitIds.Clone();
        X = x;
        Y = y;
        Queued = queued;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public IReadOnlyList<int> UnitIds => _unitIds;

    // Target point, milli-cells.
    public int X { get; }
    public int Y { get; }

    // order-queue.md: given with Shift — joins the queue of a busy unit.
    public bool Queued { get; }

    public void WritePayload(BinaryWriter writer)
    {
        UnitSelection.Write(writer, _unitIds);
        writer.Write(X);
        writer.Write(Y);
        writer.Write(Queued);
    }

    public static AttackMoveCommand Read(int playerId, int tick, BinaryReader reader) =>
        new AttackMoveCommand(playerId, tick, UnitSelection.Read(reader), reader.ReadInt32(), reader.ReadInt32(), reader.ReadBoolean());

    public void Execute(SimState state, List<SimEvent> events)
    {
        (int x, int y) = state.Map.ClampMilli(X, Y);

        bool full = false;
        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
        {
            UnitOrder order = Fights(unit) ? UnitOrder.AttackMoveTo(x, y) : UnitOrder.MoveTo(x, y);
            if (!UnitOrders.Defer(unit, Queued, order, null, ref full))
                UnitOrders.Replace(unit, order);
        }
        if (full)
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
    }

    // Who fights on the way: armed, not a worker, not a scout. The UI asks the same for its tip.
    public static bool Fights(Unit unit) =>
        unit.Worker == null && Combat.CanAttack(unit) && !unit.Stats.HasTag(UnitTags.Scout);
}

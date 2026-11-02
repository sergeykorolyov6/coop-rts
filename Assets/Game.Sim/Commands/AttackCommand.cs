using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// combat.md: right-click with the selection on an enemy unit or building.
public sealed class AttackCommand : ICommand
{
    public const int Id = 6;

    private readonly int[] _unitIds;

    public AttackCommand(int playerId, int tick, int[] unitIds, int targetId)
    {
        PlayerId = playerId;
        Tick = tick;
        _unitIds = (int[])unitIds.Clone();
        TargetId = targetId;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public IReadOnlyList<int> UnitIds => _unitIds;
    public int TargetId { get; }

    public void WritePayload(BinaryWriter writer)
    {
        UnitSelection.Write(writer, _unitIds);
        writer.Write(TargetId);
    }

    public static AttackCommand Read(int playerId, int tick, BinaryReader reader) =>
        new AttackCommand(playerId, tick, UnitSelection.Read(reader), reader.ReadInt32());

    public void Execute(SimState state, List<SimEvent> events)
    {
        Entity? target = state.FindEntity(TargetId);
        if (target == null || target.IsDead || !state.Relations.IsEnemy(PlayerId, target.OwnerId))
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }
        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
        {
            if (!Combat.CanAttack(unit))
                continue;
            UnitOrders.Give(unit, UnitOrder.Attack(target.Id));
            unit.TargetId = target.Id;
        }
    }
}

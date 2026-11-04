using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// factions.md: the selected own units that have the ability and have it ready use it; Blink
// goes towards (X, Y) in milli-cells, Boost ignores the point.
public sealed class UseAbilityCommand : ICommand
{
    public const int Id = 12;

    private readonly int[] _unitIds;

    public UseAbilityCommand(int playerId, int tick, int[] unitIds, string abilityId, int x, int y)
    {
        PlayerId = playerId;
        Tick = tick;
        _unitIds = (int[])unitIds.Clone();
        AbilityId = abilityId;
        X = x;
        Y = y;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public IReadOnlyList<int> UnitIds => _unitIds;
    public string AbilityId { get; }
    public int X { get; }
    public int Y { get; }

    public void WritePayload(BinaryWriter writer)
    {
        UnitSelection.Write(writer, _unitIds);
        writer.Write(AbilityId);
        writer.Write(X);
        writer.Write(Y);
    }

    public static UseAbilityCommand Read(int playerId, int tick, BinaryReader reader) =>
        new UseAbilityCommand(playerId, tick, UnitSelection.Read(reader), reader.ReadString(), reader.ReadInt32(), reader.ReadInt32());

    public void Execute(SimState state, List<SimEvent> events)
    {
        if (!state.Data.TryGetAbility(AbilityId, out AbilityStats? ability))
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }
        bool any = false;
        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
            any |= AbilitySystem.TryUse(state, unit, ability!, X, Y);
        if (!any)
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.AbilityNotReady));
    }
}

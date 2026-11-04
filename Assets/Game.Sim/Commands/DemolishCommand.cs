using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// demolish.md: start taking down an own building, or cancel it when it is already going.
public sealed class DemolishCommand : ICommand
{
    public const int Id = 11;

    public DemolishCommand(int playerId, int tick, int buildingId)
    {
        PlayerId = playerId;
        Tick = tick;
        BuildingId = buildingId;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public int BuildingId { get; }

    public void WritePayload(BinaryWriter writer) => writer.Write(BuildingId);

    public static DemolishCommand Read(int playerId, int tick, BinaryReader reader) =>
        new DemolishCommand(playerId, tick, reader.ReadInt32());

    public void Execute(SimState state, List<SimEvent> events)
    {
        Building? b = state.FindBuilding(BuildingId);
        if (b == null || b.IsDead || b.OwnerId != PlayerId || b.Stats.Invulnerable)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }
        b.DemolishTicksLeft = b.DemolishTicksLeft > 0 ? 0 : DemolishSystem.DemolishTicks(state, b.Stats);
    }
}

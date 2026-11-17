using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// factory.md: drop the last unit of the queue and get its full price back — if it was paid
// (factory-auto.md: an unpaid repeat goes for nothing).
public sealed class CancelProductionCommand : ICommand
{
    public const int Id = 8;

    public CancelProductionCommand(int playerId, int tick, int factoryId)
    {
        PlayerId = playerId;
        Tick = tick;
        FactoryId = factoryId;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public int FactoryId { get; }

    public void WritePayload(BinaryWriter writer) => writer.Write(FactoryId);

    public static CancelProductionCommand Read(int playerId, int tick, BinaryReader reader) =>
        new CancelProductionCommand(playerId, tick, reader.ReadInt32());

    public void Execute(SimState state, List<SimEvent> events)
    {
        Building? b = state.FindBuilding(FactoryId);
        if (b == null || b.IsDead || b.Factory == null || b.OwnerId != PlayerId || b.Factory.Queue.Count == 0)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }
        int last = b.Factory.Queue.Count - 1;
        UnitStats stats = state.Data.Unit(b.Factory.Queue[last]);
        bool paid = b.Factory.Paid[last];
        b.Factory.RemoveAt(last);
        if (!paid)
            return;

        Wallet wallet = ProduceCommand.Payer(state, b);
        wallet.Add(Resources.Primary, stats.CostPrimary);
        if (stats.CostSecondary > 0)
            wallet.Add(Resources.Secondary, stats.CostSecondary);
    }
}

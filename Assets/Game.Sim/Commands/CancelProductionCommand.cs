using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// factory.md: drop the last unit of the queue and get its full price back.
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
        if (b == null || b.IsDead || b.Factory == null || b.OwnerId != PlayerId || b.Factory._queue.Count == 0)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }
        List<string> queue = b.Factory._queue;
        UnitStats stats = state.Data.Unit(queue[queue.Count - 1]);
        queue.RemoveAt(queue.Count - 1);
        if (queue.Count == 0)
            b.Factory.Progress = 0;

        Wallet wallet = ProduceCommand.Payer(state, b);
        wallet.Add(Resources.Primary, stats.CostPrimary);
        if (stats.CostSecondary > 0)
            wallet.Add(Resources.Secondary, stats.CostSecondary);
    }
}

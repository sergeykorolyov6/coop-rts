using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// factory.md: queue a unit at an own finished factory; paid at once, from the factory's stock
// when belts supply it (M2), otherwise from the owner's wallet.
public sealed class ProduceCommand : ICommand
{
    public const int Id = 7;

    public ProduceCommand(int playerId, int tick, int factoryId, string unitTypeId)
    {
        PlayerId = playerId;
        Tick = tick;
        FactoryId = factoryId;
        UnitTypeId = unitTypeId;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public int FactoryId { get; }
    public string UnitTypeId { get; }

    public void WritePayload(BinaryWriter writer)
    {
        writer.Write(FactoryId);
        writer.Write(UnitTypeId);
    }

    public static ProduceCommand Read(int playerId, int tick, BinaryReader reader) =>
        new ProduceCommand(playerId, tick, reader.ReadInt32(), reader.ReadString());

    public void Execute(SimState state, List<SimEvent> events)
    {
        RejectReason? reason = Check(state, out Building? factory, out UnitStats? stats);
        if (reason != null)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, reason.Value));
            return;
        }
        Wallet wallet = Payer(state, factory!);
        wallet.TryPay(stats!.CostPrimary, stats.CostSecondary);
        factory!.Factory!._queue.Add(stats.Id);
    }

    private RejectReason? Check(SimState state, out Building? factory, out UnitStats? stats)
    {
        stats = null;
        factory = state.FindBuilding(FactoryId);
        if (factory == null || factory.IsDead || factory.Factory == null || factory.OwnerId != PlayerId)
            return RejectReason.NotAllowed;
        if (!factory.IsComplete || !Contains(factory.Stats.Produces, UnitTypeId)
            || factory.Factory._queue.Count >= state.Rules.FactoryQueue
            || !state.Data.TryGetUnit(UnitTypeId, out stats))
            return RejectReason.CannotProduce;

        Wallet wallet = Payer(state, factory);
        RejectReason low = factory.Factory.Stock != null ? RejectReason.FactoryStockLow : RejectReason.NotEnoughResources;
        return wallet.CanPay(stats!.CostPrimary, stats.CostSecondary) ? null : low;
    }

    // factory.md M2: the factory's own stock, or the owner's wallet when it has none.
    internal static Wallet Payer(SimState state, Building factory) =>
        factory.Factory!.Stock ?? state.GetPlayer(factory.OwnerId).Wallet;

    private static bool Contains(IReadOnlyList<string> list, string value)
    {
        foreach (string s in list)
        {
            if (s == value)
                return true;
        }
        return false;
    }
}

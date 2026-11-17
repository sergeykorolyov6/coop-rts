using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// factory-auto.md: switch an own finished factory's repeat. Off drops the unpaid places of the
// queue (nothing was paid for them); the paid ones are made once.
public sealed class SetRepeatCommand : ICommand
{
    public const int Id = 19;

    public SetRepeatCommand(int playerId, int tick, int factoryId, bool on)
    {
        PlayerId = playerId;
        Tick = tick;
        FactoryId = factoryId;
        On = on;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public int FactoryId { get; }
    public bool On { get; }

    public void WritePayload(BinaryWriter writer)
    {
        writer.Write(FactoryId);
        writer.Write(On);
    }

    public static SetRepeatCommand Read(int playerId, int tick, BinaryReader reader) =>
        new SetRepeatCommand(playerId, tick, reader.ReadInt32(), reader.ReadBoolean());

    public void Execute(SimState state, List<SimEvent> events)
    {
        Building? b = state.FindBuilding(FactoryId);
        if (b == null || b.IsDead || b.Factory == null || b.OwnerId != PlayerId || !b.IsComplete)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }
        Factory factory = b.Factory;
        factory.Repeat = On;
        if (On)
            return;
        for (int i = factory.Queue.Count - 1; i >= 0; i--)
        {
            if (!factory.Paid[i])
                factory.RemoveAt(i);
        }
    }
}

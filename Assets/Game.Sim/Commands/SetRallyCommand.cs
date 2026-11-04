using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// factory.md M2: set (or clear) where an own factory's finished units walk to.
public sealed class SetRallyCommand : ICommand
{
    public const int Id = 10;

    public SetRallyCommand(int playerId, int tick, int factoryId, bool hasRally, int x, int y)
    {
        PlayerId = playerId;
        Tick = tick;
        FactoryId = factoryId;
        HasRally = hasRally;
        X = x;
        Y = y;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public int FactoryId { get; }
    public bool HasRally { get; }

    // Milli-cells.
    public int X { get; }
    public int Y { get; }

    public void WritePayload(BinaryWriter writer)
    {
        writer.Write(FactoryId);
        writer.Write(HasRally);
        writer.Write(X);
        writer.Write(Y);
    }

    public static SetRallyCommand Read(int playerId, int tick, BinaryReader reader) =>
        new SetRallyCommand(playerId, tick, reader.ReadInt32(), reader.ReadBoolean(), reader.ReadInt32(), reader.ReadInt32());

    public void Execute(SimState state, List<SimEvent> events)
    {
        Building? b = state.FindBuilding(FactoryId);
        bool outside = HasRally && (X < 0 || Y < 0 || X >= Grid.CellToMilli(state.Map.Width) || Y >= Grid.CellToMilli(state.Map.Height));
        if (b == null || b.IsDead || b.Factory == null || b.OwnerId != PlayerId || outside)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }
        b.Factory.HasRally = HasRally;
        b.Factory.RallyX = HasRally ? X : 0;
        b.Factory.RallyY = HasRally ? Y : 0;
    }
}

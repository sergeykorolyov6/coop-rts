using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// coop-tools.md: a mark on the map for the allies. A command, so that it reaches every machine the
// way orders do and stays in the replay; it changes nothing in the state and only raises Pinged.
public sealed class PingCommand : ICommand
{
    public const int Id = 15;

    public PingCommand(int playerId, int tick, int x, int y)
    {
        PlayerId = playerId;
        Tick = tick;
        X = x;
        Y = y;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;

    // Milli-cells.
    public int X { get; }
    public int Y { get; }

    public void WritePayload(BinaryWriter writer)
    {
        writer.Write(X);
        writer.Write(Y);
    }

    public static PingCommand Read(int playerId, int tick, BinaryReader reader) =>
        new PingCommand(playerId, tick, reader.ReadInt32(), reader.ReadInt32());

    public void Execute(SimState state, List<SimEvent> events)
    {
        bool outside = X < 0 || Y < 0 || X >= Grid.CellToMilli(state.Map.Width) || Y >= Grid.CellToMilli(state.Map.Height);
        if (outside || !state.IsTeamPlayer(PlayerId))
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }
        events.Add(new Pinged(state.Tick, PlayerId, X, Y));
    }
}

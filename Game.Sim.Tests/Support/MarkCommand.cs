using System.Collections.Generic;
using System.IO;

namespace Game.Sim.Tests;

// Test-only: raises MarkEvent, which makes execution order observable.
internal sealed class MarkCommand : ICommand
{
    public const int Id = 1003;

    public MarkCommand(int playerId, int tick, string label)
    {
        PlayerId = playerId;
        Tick = tick;
        Label = label;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public string Label { get; }

    public void WritePayload(BinaryWriter writer) => writer.Write(Label);

    public static MarkCommand Read(int playerId, int tick, BinaryReader reader) =>
        new MarkCommand(playerId, tick, reader.ReadString());

    public void Execute(SimState state, List<SimEvent> events) =>
        events.Add(new MarkEvent(state.Tick, PlayerId, Label));
}

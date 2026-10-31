using System.Collections.Generic;
using System.IO;

namespace Game.Sim.Tests;

// Test-only: there are no gameplay commands in M0, and a "give resources" command
// would be a mechanic nobody specified. Test type ids start at 1000.
internal sealed class GrantCommand : ICommand
{
    public const int Id = 1001;

    public GrantCommand(int playerId, int tick, int resourceType, int amount)
    {
        PlayerId = playerId;
        Tick = tick;
        ResourceType = resourceType;
        Amount = amount;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public int ResourceType { get; }
    public int Amount { get; }

    public void WritePayload(BinaryWriter writer)
    {
        writer.Write(ResourceType);
        writer.Write(Amount);
    }

    public static GrantCommand Read(int playerId, int tick, BinaryReader reader) =>
        new GrantCommand(playerId, tick, reader.ReadInt32(), reader.ReadInt32());

    public void Execute(SimState state, List<SimEvent> events) =>
        state.GetPlayer(PlayerId).Wallet.Add(ResourceType, Amount);
}

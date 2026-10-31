using System.Collections.Generic;
using System.IO;

namespace Game.Sim.Tests;

// Test-only: draws from the match RNG, so the seed shows up in the state.
internal sealed class RandomGrantCommand : ICommand
{
    public const int Id = 1002;

    public RandomGrantCommand(int playerId, int tick, int maxAmount)
    {
        PlayerId = playerId;
        Tick = tick;
        MaxAmount = maxAmount;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public int MaxAmount { get; }

    public void WritePayload(BinaryWriter writer) => writer.Write(MaxAmount);

    public static RandomGrantCommand Read(int playerId, int tick, BinaryReader reader) =>
        new RandomGrantCommand(playerId, tick, reader.ReadInt32());

    public void Execute(SimState state, List<SimEvent> events) =>
        state.GetPlayer(PlayerId).Wallet.Add(Setups.Primary, state.Rng.NextInt(1, MaxAmount + 1));
}

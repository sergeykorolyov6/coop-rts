using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// coop-tools.md: one-off help from wallet to wallet for a team ally. Not income: the receiver's
// Earned (what its headquarters accepted) stays as it was.
public sealed class GiveResourcesCommand : ICommand
{
    public const int Id = 16;

    public GiveResourcesCommand(int playerId, int tick, int toPlayerId, int resourceType, int amount)
    {
        PlayerId = playerId;
        Tick = tick;
        ToPlayerId = toPlayerId;
        ResourceType = resourceType;
        Amount = amount;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public int ToPlayerId { get; }
    public int ResourceType { get; }
    public int Amount { get; }

    public void WritePayload(BinaryWriter writer)
    {
        writer.Write(ToPlayerId);
        writer.Write(ResourceType);
        writer.Write(Amount);
    }

    public static GiveResourcesCommand Read(int playerId, int tick, BinaryReader reader) =>
        new GiveResourcesCommand(playerId, tick, reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

    public void Execute(SimState state, List<SimEvent> events)
    {
        bool allowed = Amount > 0
            && ResourceType >= 0 && ResourceType < state.ResourceTypeCount
            && ToPlayerId != PlayerId
            && state.IsTeamPlayer(PlayerId) && state.IsTeamPlayer(ToPlayerId)
            && state.Relations.IsAlly(PlayerId, ToPlayerId);
        if (!allowed)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }
        // All or nothing, against the wallet as it is on this tick.
        if (!state.GetPlayer(PlayerId).Wallet.TrySpend(ResourceType, Amount))
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotEnoughResources));
            return;
        }
        state.GetPlayer(ToPlayerId).Wallet.Add(ResourceType, Amount);
        events.Add(new ResourcesGiven(state.Tick, PlayerId, ToPlayerId, ResourceType, Amount));
    }
}

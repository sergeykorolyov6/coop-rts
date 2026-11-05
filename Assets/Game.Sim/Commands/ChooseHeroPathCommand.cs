using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// factions.md (Yellow Star): pick the hero's path — once a mission, alive or waiting to come back.
public sealed class ChooseHeroPathCommand : ICommand
{
    public const int Id = 13;

    public ChooseHeroPathCommand(int playerId, int tick, string pathId)
    {
        PlayerId = playerId;
        Tick = tick;
        PathId = pathId;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public string PathId { get; }

    public void WritePayload(BinaryWriter writer) => writer.Write(PathId);

    public static ChooseHeroPathCommand Read(int playerId, int tick, BinaryReader reader) =>
        new ChooseHeroPathCommand(playerId, tick, reader.ReadString());

    public void Execute(SimState state, List<SimEvent> events)
    {
        Player player = state.GetPlayer(PlayerId);
        string hero = player.Faction?.HeroUnitId ?? "";
        if (player.HeroPathId.Length > 0)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.HeroPathChosen));
            return;
        }
        if (hero.Length == 0 || !state.Data.TryGetHeroPath(PathId, out HeroPathStats? path) || path!.HeroUnitId != hero)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }
        player.HeroPathId = PathId;
        Unit? living = state.FindHero(PlayerId);
        if (living != null)
            HeroSystem.ApplyLevel(state, living, player.HeroLevel);
    }
}

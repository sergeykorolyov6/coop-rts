using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace Game.Sim.Tests;

// What the lockstep (network.md) needs from the sim: a command read as the player and tick the
// host decides, the lobby's factions, and the same hashes on machines with different system cultures.
public class NetworkReadinessTests
{
    [Test]
    public void ReadAs_TakesPlayerAndTickFromCaller_NotFromHeader()
    {
        CommandCodec codec = GameCommands.CreateCodec();
        byte[] encoded = codec.Encode(new ChooseHeroPathCommand(1, 5, "ys_mage"));

        var command = (ChooseHeroPathCommand)codec.ReadAs(encoded, 2, 40);

        Assert.That((command.PlayerId, command.Tick, command.PathId), Is.EqualTo((2, 40, "ys_mage")));
    }

    [Test]
    public void ReadAs_TrailingBytes_Rejected()
    {
        CommandCodec codec = GameCommands.CreateCodec();
        byte[] encoded = codec.Encode(new ChooseHeroPathCommand(1, 5, "ys_mage")).Append((byte)0).ToArray();

        Assert.Throws<System.IO.InvalidDataException>(() => codec.ReadAs(encoded, 1, 0));
    }

    [Test]
    public void CreateSetup_LobbyFactions_OverrideMission()
    {
        MissionStats coop = CoopMissionTests.Catalog().Find("coop")!;
        var setup = MissionLauncher.CreateSetup(RepoData.Get(), coop, CoopMissionTests.MapJson(coop.Map), 7,
            new[] { "yellow_star", "brown_scavengers" });
        var sim = new Simulation(setup);

        Assert.That(sim.State.GetPlayer(1).Faction!.Id, Is.EqualTo("yellow_star"));
        Assert.That(sim.State.GetPlayer(2).Faction!.Id, Is.EqualTo("brown_scavengers"));
        Assert.That(sim.State.FindHero(1), Is.Not.Null);
    }

    // Russian Windows next to an English Mac: data parsing and the sim must not depend on culture.
    [Test]
    public void SameMission_SameHashes_UnderDifferentCultures()
    {
        List<ulong> Run(string culture)
        {
            CultureInfo before = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
                MissionStats coop = CoopMissionTests.Catalog().Find("coop")!;
                var sim = new Simulation(MissionLauncher.CreateSetup(RepoData.Load(), coop, CoopMissionTests.MapJson(coop.Map), 11,
                    new[] { "red_lightning", "yellow_star" }));
                Unit builder = sim.State.FindBuilder(1)!;
                Unit hero = sim.State.FindHero(2)!;
                sim.Enqueue(new MoveCommand(1, 0, new[] { builder.Id }, builder.X + 4000, builder.Y + 3000));
                sim.Enqueue(new ChooseHeroPathCommand(2, 0, "ys_warrior"));
                sim.Enqueue(new MoveCommand(2, 20, new[] { hero.Id }, hero.X - 5000, hero.Y + 2000));
                var hashes = new List<ulong>();
                for (int i = 0; i < 400; i++)
                {
                    sim.Tick();
                    hashes.Add(sim.StateHash);
                }
                return hashes;
            }
            finally
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = before;
            }
        }

        List<ulong> invariant = Run("");
        Assert.That(Run("ru-RU"), Is.EqualTo(invariant));
        Assert.That(Run("tr-TR"), Is.EqualTo(invariant));
        Assert.That(Run("ar-SA"), Is.EqualTo(invariant));
    }
}

using System.Collections.Generic;
using System.Linq;
using Game.Net;
using NUnit.Framework;

namespace Game.Sim.Tests;

// mechanics/network.md: lobby, turns, stalls, desync and leaving, host and players in memory
// (LoopbackConnection) and once over real TCP.
public class LockstepTests
{
    private const int Delay = LockstepSettings.DelayTurns;
    private const int TurnTicks = LockstepSettings.TurnTicks;

    private static ulong Content => 12345;

    // A host (Relay + its own client) and joined clients, all in memory.
    private sealed class Match
    {
        public readonly Relay Relay;
        public readonly List<LockstepClient> Clients = new List<LockstepClient>();
        public readonly List<Simulation> Sims = new List<Simulation>();

        public Match(string missionId = "coop", int players = 2, ulong? joinContent = null)
        {
            Relay = new Relay(RepoData.Get(), RepoData.Catalog(), Content, missionId);
            for (int i = 0; i < players; i++)
                Join(i == 0 ? Content : joinContent ?? Content);
            Pump();
        }

        public LockstepClient Join(ulong content)
        {
            (LoopbackConnection host, LoopbackConnection player) = LoopbackConnection.CreatePair();
            Relay.Add(host);
            var client = new LockstepClient(player, $"P{Clients.Count + 1}", content, GameCommands.CreateCodec());
            Clients.Add(client);
            return client;
        }

        public void Pump()
        {
            for (int i = 0; i < 4; i++)
            {
                Relay.Poll();
                foreach (LockstepClient c in Clients)
                    c.Poll();
            }
        }

        public void Start(int seed = 77)
        {
            Clients[0].RequestStart(seed);
            Pump();
            foreach (LockstepClient c in Clients)
                Sims.Add(Launch(c.Start!));
        }

        // As SimRunner does: a new match, or the save restored up to its tick.
        public static Simulation Launch(MatchStart start)
        {
            MatchSetup setup = RepoData.Setup(start.MissionId, start.Seed, start.Factions);
            return start.Save != null ? SaveGame.Read(start.Save).Restore(setup, GameCommands.CreateCodec()) : new Simulation(setup);
        }

        // Every machine tries one tick, then the messages travel.
        public void StepAll()
        {
            for (int i = 0; i < Clients.Count; i++)
                Clients[i].TryStep(Sims[i]);
            Pump();
        }
    }

    [Test]
    public void GWT1_Start_SameMissionSeedAndFactions_EachOwnSlot()
    {
        var m = new Match();
        m.Clients[1].PickFaction("yellow_star");
        m.Pump();
        m.Start(seed: 99);

        Assert.That(m.Clients.Select(c => c.Slot), Is.EqualTo(new[] { 1, 2 }));
        foreach (LockstepClient c in m.Clients)
        {
            Assert.That(c.Start!.MissionId, Is.EqualTo("coop"));
            Assert.That(c.Start.Seed, Is.EqualTo(99));
            Assert.That(c.Start.Factions, Is.EqualTo(new[] { "blue_walls", "yellow_star" }));
        }
        Assert.That(m.Sims[0].StateHash, Is.EqualTo(m.Sims[1].StateHash));
    }

    [Test]
    public void GWT2_CommandGivenAtTurnStart_RunsDelayTurnsLater_OnEveryMachine()
    {
        var m = new Match();
        m.Clients[0].PickFaction("yellow_star");
        m.Pump();
        m.Start();
        while (m.Sims[0].State.Tick < 10)
            m.StepAll();
        Assert.That(m.Sims[1].State.Tick, Is.EqualTo(10));

        m.Clients[0].Submit(new ChooseHeroPathCommand(1, 0, "ys_mage"));
        int expected = (10 / TurnTicks + Delay) * TurnTicks;
        Assert.That(expected, Is.EqualTo(16));
        while (m.Sims[0].State.Tick < expected)
            m.StepAll();
        Assert.That(m.Sims.Select(s => s.State.GetPlayer(1).HeroPathId), Is.All.Empty);
        m.StepAll();
        Assert.That(m.Sims.Select(s => s.State.GetPlayer(1).HeroPathId), Is.All.EqualTo("ys_mage"));
    }

    [Test]
    public void GWT3_BothPlayersGiveOrders_HashesEqualEveryTick_AndEqualToOfflineRun()
    {
        var m = new Match();
        m.Start(seed: 5);
        Simulation offline = RepoData.Launch("coop", 5, m.Clients[0].Start!.Factions);
        Unit b1 = offline.State.FindBuilder(1)!, b2 = offline.State.FindBuilder(2)!;

        for (int tick = 0; tick < 300; tick++)
        {
            if (tick % 40 == 0)
            {
                int dx = (tick / 40 % 2 == 0 ? 1 : -1) * 3000;
                var c1 = new MoveCommand(1, 0, new[] { b1.Id }, b1.X + dx, b1.Y);
                var c2 = new MoveCommand(2, 0, new[] { b2.Id }, b2.X, b2.Y + dx);
                m.Clients[0].Submit(c1);
                m.Clients[1].Submit(c2);
                int runAt = (tick / TurnTicks + Delay) * TurnTicks;
                offline.Enqueue(new MoveCommand(1, runAt, new[] { b1.Id }, b1.X + dx, b1.Y));
                offline.Enqueue(new MoveCommand(2, runAt, new[] { b2.Id }, b2.X, b2.Y + dx));
            }
            m.StepAll();
            offline.Tick();
            Assert.That(m.Sims[0].State.Tick, Is.EqualTo(tick + 1));
            Assert.That(m.Sims[1].StateHash, Is.EqualTo(m.Sims[0].StateHash), $"tick {tick + 1}");
            Assert.That(offline.StateHash, Is.EqualTo(m.Sims[0].StateHash), $"tick {tick + 1}");
        }
        Assert.That(m.Clients.SelectMany(c => c.TakeNotices()), Is.Empty);
    }

    [Test]
    public void GWT4_PlayerSilent_OthersWaitAtThatTurn_ThenGoOn()
    {
        var m = new Match();
        m.Start();
        for (int i = 0; i < 20; i++)
        {
            m.Clients[0].TryStep(m.Sims[0]);
            m.Pump();
        }
        Assert.That(m.Sims[0].State.Tick, Is.EqualTo(Delay * TurnTicks));
        Assert.That(m.Clients[0].Waiting, Is.True);

        m.Clients[1].TryStep(m.Sims[1]);
        m.Pump();
        for (int i = 0; i < 20; i++)
        {
            m.Clients[0].TryStep(m.Sims[0]);
            m.Pump();
        }
        Assert.That(m.Sims[0].State.Tick, Is.EqualTo((Delay + 1) * TurnTicks));
    }

    [Test]
    public void GWT5_CommandClaimingAnotherPlayer_RunsAsItsSender()
    {
        var m = new Match();
        m.Clients[0].PickFaction("yellow_star");
        m.Clients[1].PickFaction("yellow_star");
        m.Pump();
        m.Start();
        m.Clients[1].Submit(new ChooseHeroPathCommand(1, 0, "ys_mage"));
        for (int i = 0; i < 20; i++)
            m.StepAll();

        foreach (Simulation s in m.Sims)
        {
            Assert.That(s.State.GetPlayer(2).HeroPathId, Is.EqualTo("ys_mage"));
            Assert.That(s.State.GetPlayer(1).HeroPathId, Is.Empty);
        }
    }

    [Test]
    public void GWT6_MachinesDiverge_EveryoneToldDesyncAtTheCheck()
    {
        var m = new Match();
        m.Start();
        // Past the network: only the second machine's state changes.
        m.Sims[1].Enqueue(new MoveCommand(2, 1, new[] { m.Sims[1].State.FindBuilder(2)!.Id }, 1000, 1000));
        int check = TurnTicks * LockstepSettings.HashEveryTurns;
        while (m.Sims[0].State.Tick < check + TurnTicks)
            m.StepAll();

        foreach (LockstepClient c in m.Clients)
        {
            NetNotice notice = c.TakeNotices().Single();
            Assert.That((notice.Kind, notice.Tick), Is.EqualTo((NetNoticeKind.Desync, check)));
        }
    }

    [Test]
    public void GWT7_PlayerLeaves_HostToldAndGoesOnWithoutWaiting()
    {
        var m = new Match();
        m.Start();
        for (int i = 0; i < 10; i++)
            m.StepAll();
        m.Clients[1].Leave();
        m.Pump();
        for (int i = 0; i < 40; i++)
        {
            m.Clients[0].TryStep(m.Sims[0]);
            m.Pump();
        }

        NetNotice notice = m.Clients[0].TakeNotices().Single();
        Assert.That((notice.Kind, notice.Slot), Is.EqualTo((NetNoticeKind.PlayerLeft, 2)));
        Assert.That(m.Sims[0].State.Tick, Is.EqualTo(50));
    }

    [Test]
    public void GWT8_HostLeaves_PlayerToldHostLost()
    {
        var m = new Match();
        m.Start();
        m.StepAll();
        m.Clients[0].Leave();
        m.Pump();

        Assert.That(m.Relay.Closed, Is.True);
        Assert.That(m.Clients[1].Disconnected, Is.True);
        Assert.That(m.Clients[1].TakeNotices().Single().Kind, Is.EqualTo(NetNoticeKind.HostLost));
    }

    [Test]
    public void GWT9_OtherContentHash_Rejected()
    {
        var m = new Match(players: 2, joinContent: 999);

        NetNotice notice = m.Clients[1].TakeNotices().Single();
        Assert.That((notice.Kind, notice.Text), Is.EqualTo((NetNoticeKind.Rejected, "Другая версия игры")));
        Assert.That(m.Clients[1].Disconnected, Is.True);
        Assert.That(m.Relay.PlayerCount, Is.EqualTo(1));
    }

    [Test]
    public void GWT10_FewerPlayersThanPlaces_DoesNotStart()
    {
        var m = new Match(players: 1);
        m.Clients[0].RequestStart(1);
        m.Pump();

        Assert.That(m.Relay.Started, Is.False);
        Assert.That(m.Clients[0].Start, Is.Null);
    }

    [Test]
    public void GWT11_RealTcpOnLoopback_HashesEqual()
    {
        using var server = new TcpServer(0);
        var relay = new Relay(RepoData.Get(), RepoData.Catalog(), Content, "coop");
        (LoopbackConnection hostSide, LoopbackConnection hostPlayer) = LoopbackConnection.CreatePair();
        relay.Add(hostSide);
        var host = new LockstepClient(hostPlayer, "Host", Content, GameCommands.CreateCodec());
        var connector = new TcpConnector("127.0.0.1", server.Port);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!connector.IsDone && watch.ElapsedMilliseconds < 5000)
            System.Threading.Thread.Sleep(1);
        var guest = new LockstepClient(connector.Connection!, "Guest", Content, GameCommands.CreateCodec());
        var clients = new[] { host, guest };

        void Pump()
        {
            for (int i = 0; i < 3; i++)
            {
                while (server.TryAccept(out IConnection? accepted))
                    relay.Add(accepted!);
                relay.Poll();
                foreach (LockstepClient c in clients)
                    c.Poll();
                System.Threading.Thread.Sleep(1);
            }
        }

        while (guest.Slot == 0 && watch.ElapsedMilliseconds < 5000)
            Pump();
        host.RequestStart(3);
        while (guest.Start == null && watch.ElapsedMilliseconds < 5000)
            Pump();
        Assert.That(guest.Start, Is.Not.Null, "start did not arrive over TCP");
        Simulation[] sims = clients.Select(c => Match.Launch(c.Start!)).ToArray();
        Unit b2 = sims[1].State.FindBuilder(2)!;
        int startX = b2.X;
        guest.Submit(new MoveCommand(2, 0, new[] { b2.Id }, b2.X + 4000, b2.Y));

        while (sims.Min(s => s.State.Tick) < 100 && watch.ElapsedMilliseconds < 20000)
        {
            for (int i = 0; i < 2; i++)
            {
                if (sims[i].State.Tick < 100)
                    clients[i].TryStep(sims[i]);
            }
            Pump();
        }

        Assert.That(sims.Select(s => s.State.Tick), Is.All.EqualTo(100));
        Assert.That(sims[0].StateHash, Is.EqualTo(sims[1].StateHash));
        Assert.That(sims[0].State.FindBuilder(2)!.X, Is.Not.EqualTo(startX));
        server.Dispose();
    }

    // A coop match of 501 ticks (not a whole turn: the resume must handle it), saved.
    private static (SaveGame Save, Simulation Original) SavedCoop()
    {
        Simulation sim = RepoData.Launch("coop", 21, new[] { "red_lightning", "yellow_star" });
        Unit b1 = sim.State.FindBuilder(1)!;
        sim.Enqueue(new MoveCommand(1, 10, new[] { b1.Id }, b1.X + 4000, b1.Y));
        sim.Enqueue(new ChooseHeroPathCommand(2, 30, "ys_warrior"));
        sim.Run(501);
        return (SaveGame.Read(SaveGame.Capture(sim, "coop", 21, GameCommands.CreateCodec()).Write()), sim);
    }

    [Test]
    public void SaveGWT8_HostPicksSave_EveryoneRestoresIt_AndGoesOnInStep()
    {
        (SaveGame save, Simulation original) = SavedCoop();
        var m = new Match();
        m.Clients[0].PickSave(save.Write());
        m.Pump();
        Assert.That(m.Clients[1].LobbySaveTick, Is.EqualTo(501));
        m.Start(seed: 999);

        foreach (Simulation s in m.Sims)
        {
            Assert.That(s.State.Tick, Is.EqualTo(501));
            Assert.That(s.StateHash, Is.EqualTo(save.StateHash));
            Assert.That(s.State.GetPlayer(2).HeroPathId, Is.EqualTo("ys_warrior"));
        }
        Assert.That(m.Clients[1].Start!.Seed, Is.EqualTo(21), "the save's seed, not the host's new one");

        Unit b2 = m.Sims[1].State.FindBuilder(2)!;
        int firstTurn = LockstepSettings.FirstTurn(501);
        int runAt = (firstTurn + Delay) * TurnTicks;
        var command = new MoveCommand(2, runAt, new[] { b2.Id }, b2.X - 3000, b2.Y);
        m.Clients[1].Submit(command);
        original.Enqueue(command);
        for (int i = 0; i < 200; i++)
        {
            m.StepAll();
            original.Tick();
            Assert.That(m.Sims[0].StateHash, Is.EqualTo(original.StateHash), $"tick {original.State.Tick}");
            Assert.That(m.Sims[1].StateHash, Is.EqualTo(original.StateHash), $"tick {original.State.Tick}");
        }
        Assert.That(m.Clients.SelectMany(c => c.TakeNotices()), Is.Empty);
    }

    [Test]
    public void SaveGWT9_SavePicked_FactionsFixed()
    {
        (SaveGame save, _) = SavedCoop();
        var m = new Match();
        m.Clients[0].PickSave(save.Write());
        m.Pump();
        m.Clients[1].PickFaction("blue_walls");
        m.Pump();

        Assert.That(m.Clients[0].Lobby.Select(e => e.FactionId), Is.EqualTo(new[] { "red_lightning", "yellow_star" }));
    }

    [Test]
    public void SaveGWT10_SaveForTwo_LobbyOfOne_DoesNotStart()
    {
        (SaveGame save, _) = SavedCoop();
        var m = new Match(players: 1);
        m.Clients[0].PickSave(save.Write());
        m.Pump();
        m.Clients[0].RequestStart(1);
        m.Pump();

        Assert.That(m.Relay.Started, Is.False);
    }

    [Test]
    public void Edge_SaveDropped_BackToANewMatch()
    {
        (SaveGame save, _) = SavedCoop();
        var m = new Match();
        m.Clients[0].PickSave(save.Write());
        m.Pump();
        m.Clients[0].PickSave(System.Array.Empty<byte>());
        m.Pump();
        m.Start();

        Assert.That(m.Clients[1].LobbySaveTick, Is.EqualTo(-1));
        Assert.That(m.Sims.Select(s => s.State.Tick), Is.All.Zero);
    }

    [Test]
    public void Edge_UnreadableCommand_DroppedByHost_OthersGoOn()
    {
        var m = new Match();
        m.Start();
        m.Clients[1].Submit(new BrokenCommand());
        for (int i = 0; i < 20; i++)
            m.StepAll();

        Assert.That(m.Relay.DroppedCommands, Is.EqualTo(1));
        Assert.That(m.Sims.Select(s => s.State.Tick), Is.All.EqualTo(20));
        Assert.That(m.Sims[0].StateHash, Is.EqualTo(m.Sims[1].StateHash));
    }

    [Test]
    public void Edge_FactionNotForPlayers_Ignored()
    {
        var m = new Match();
        m.Clients[1].PickFaction("black_legion");
        m.Clients[1].PickFaction("no_such_faction");
        m.Pump();

        Assert.That(m.Clients[0].Lobby.Single(e => e.Slot == 2).FactionId, Is.EqualTo("red_lightning"));
    }

    [Test]
    public void Edge_PlayerLeavesLobby_PlacesRenumbered_HostStaysFirst()
    {
        var m = new Match(players: 3);
        m.Clients[1].Leave();
        m.Pump();

        Assert.That(m.Clients[0].Slot, Is.EqualTo(1));
        Assert.That(m.Clients[2].Slot, Is.EqualTo(2));
        Assert.That(m.Clients[0].Lobby.Select(e => e.Name), Is.EqualTo(new[] { "P1", "P3" }));
    }

    // A command type the game does not know: nobody can read it.
    private sealed class BrokenCommand : ICommand
    {
        public int PlayerId => 2;
        public int Tick => 0;
        public int TypeId => 999;
        public void WritePayload(System.IO.BinaryWriter writer) => writer.Write(1);
        public void Execute(SimState state, List<SimEvent> events) { }
    }
}

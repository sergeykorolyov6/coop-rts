using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/attack-move.md (U8): the player's "attack-move" — fighters walk and fight
// whatever they see on the way; builders, scouts and the unarmed just walk.
public class AttackMoveTests
{
    private const int A = Setups.PlayerA, B = Setups.PlayerB, Enemy = Setups.EnemyAi;
    private const int Second = Simulation.TicksPerSecond;

    // 24×12, open ground; headquarters of A at (2,8), of B at (18,8); the walk goes along y = 3.
    private static Simulation Field()
    {
        var map = new MapSetup(24, 12).Start(A, 2, 8).Start(B, 18, 8);
        return new Simulation(Setups.Coop(map));
    }

    private static AttackMoveCommand Go(Simulation sim, int x, int y, bool queued, params Unit[] units) =>
        new AttackMoveCommand(units[0].OwnerId, sim.State.Tick, units.Select(u => u.Id).ToArray(), x, y, queued);

    private static AttackMoveCommand Go(Simulation sim, int x, int y, params Unit[] units) => Go(sim, x, y, false, units);

    private static List<HitLanded> HitsBy(List<HitLanded> hits, Unit unit) => hits.Where(h => h.AttackerId == unit.Id).ToList();

    private static List<HitLanded> Walk(Simulation sim, System.Func<Simulation, bool> until, int maxTicks = 60 * Second)
    {
        var hits = new List<HitLanded>();
        for (int i = 0; i < maxTicks && !until(sim); i++)
        {
            sim.Tick();
            hits.AddRange(sim.Events.OfType<HitLanded>());
        }
        return hits;
    }

    private static bool At(Unit unit, int cx, int cy) => unit.CellX == cx && unit.CellY == cy;

    [Test]
    public void GWT1_RaiderHalfway_FoughtAndKilled_ThenOnToThePoint_Idle()
    {
        var sim = Field();
        Unit infantry = Spawn(sim, A, "infantry", 2, 3);
        Unit raider = Spawn(sim, Enemy, "legion_raider", 7, 3);

        sim.Order(Go(sim, Center(12), Center(3), infantry));
        List<HitLanded> hits = Walk(sim, s => At(infantry, 12, 3) && infantry.Order.Kind == UnitOrderKind.Idle);

        Assert.That(raider.IsDead, Is.True);
        Assert.That(HitsBy(hits, infantry).Any(h => h.TargetId == raider.Id), Is.True);
        Assert.That(At(infantry, 12, 3), Is.True);
        Assert.That(infantry.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
    }

    [Test]
    public void GWT2_PlainMove_PassesWithoutAStrike()
    {
        var sim = Field();
        Unit infantry = Spawn(sim, A, "infantry", 2, 3);
        Unit raider = Spawn(sim, Enemy, "legion_raider", 7, 3);

        sim.Order(sim.Move(A, Center(12), Center(3), infantry));
        List<HitLanded> hits = Walk(sim, s => At(infantry, 12, 3) && infantry.Order.Kind == UnitOrderKind.Idle);

        Assert.That(At(infantry, 12, 3), Is.True);
        Assert.That(HitsBy(hits, infantry).Where(h => h.TargetId == raider.Id), Is.Empty);
    }

    [Test]
    public void GWT3_BuilderAndInfantry_InfantryFights_BuilderJustWalks()
    {
        var sim = Field();
        Unit infantry = Spawn(sim, A, "infantry", 2, 3);
        Unit builder = sim.State.FindBuilder(A)!;
        builder.X = Center(2);
        builder.Y = Center(4);
        Spawn(sim, Enemy, "legion_raider", 7, 4);

        sim.Order(Go(sim, Center(12), Center(3), infantry, builder));
        sim.Tick();
        Assert.That(builder.Order.Kind, Is.EqualTo(UnitOrderKind.Move));
        Assert.That(infantry.Order.Kind, Is.EqualTo(UnitOrderKind.AttackMove));
        List<HitLanded> hits = Walk(sim, s => builder.Order.Kind == UnitOrderKind.Idle, 20 * Second);

        Assert.That(HitsBy(hits, builder), Is.Empty);
        Assert.That(HitsBy(hits, infantry), Is.Not.Empty);
    }

    [Test]
    public void GWT4_Scout_JustWalks()
    {
        var sim = Field();
        Unit scout = Spawn(sim, A, "scout", 2, 3);
        Spawn(sim, Enemy, "legion_raider", 7, 4);

        sim.Order(Go(sim, Center(12), Center(3), scout));
        sim.Tick();
        Assert.That(scout.Order.Kind, Is.EqualTo(UnitOrderKind.Move));
        List<HitLanded> hits = Walk(sim, s => At(scout, 12, 3), 20 * Second);

        Assert.That(HitsBy(hits, scout), Is.Empty);
    }

    [Test]
    public void GWT5_Repairer_JustWalks()
    {
        var sim = Field();
        Unit repairer = Spawn(sim, A, "repairer", 2, 3);

        sim.Order(Go(sim, Center(12), Center(3), repairer));
        sim.Tick();

        Assert.That(repairer.Order.Kind, Is.EqualTo(UnitOrderKind.Move));
    }

    [Test]
    public void GWT6_EnemyComesIntoSight_TakenAsTargetNextTurn_Struck()
    {
        var sim = Field();
        Unit infantry = Spawn(sim, A, "infantry", 2, 3);
        sim.Order(Go(sim, Center(20), Center(3), infantry));
        sim.Run(5);
        Assert.That(infantry.TargetId, Is.EqualTo(0));

        Unit gunner = Spawn(sim, Enemy, "legion_gunner", infantry.CellX + 3, 3);
        sim.Tick();
        Assert.That(infantry.TargetId, Is.EqualTo(gunner.Id));
        List<HitLanded> hits = Walk(sim, s => gunner.IsDead, 30 * Second);
        Assert.That(HitsBy(hits, infantry).Any(h => h.TargetId == gunner.Id), Is.True);
    }

    [Test]
    public void GWT7_OnlyALegionBeltInSight_Broken_ThenOn()
    {
        var sim = Field();
        Unit infantry = Spawn(sim, A, "infantry", 2, 3);
        Building belt = sim.State.PlaceBuilding(Enemy, sim.State.Data.Building("conveyor"), 6, 3, Direction.Right, complete: true);

        sim.Order(Go(sim, Center(12), Center(3), infantry));
        Walk(sim, s => At(infantry, 12, 3) && infantry.Order.Kind == UnitOrderKind.Idle);

        Assert.That(belt.IsDead, Is.True);
        Assert.That(At(infantry, 12, 3), Is.True);
    }

    [Test]
    public void GWT8_WalkingToA_ShiftAttackMoveToB_ThenB()
    {
        var sim = Field();
        Unit infantry = Spawn(sim, A, "infantry", 2, 3);
        sim.Order(sim.Move(A, Center(6), Center(3), infantry));
        sim.Order(Go(sim, Center(6), Center(6), true, infantry));
        sim.Tick();
        Assert.That(infantry.OrderQueue.Select(o => o.Order.Kind), Is.EqualTo(new[] { UnitOrderKind.AttackMove }));

        sim.RunUntil(s => infantry.Order.Kind == UnitOrderKind.AttackMove, 20 * Second);
        Assert.That(At(infantry, 6, 3), Is.True);
        sim.RunUntil(s => infantry.Order.Kind == UnitOrderKind.Idle, 20 * Second);
        Assert.That(At(infantry, 6, 6), Is.True);
    }

    [Test]
    public void GWT9_Codec_RoundTripsEveryField()
    {
        CommandCodec codec = GameCommands.CreateCodec();
        byte[] encoded = codec.Encode(new AttackMoveCommand(2, 9, new[] { 5, 7 }, 3400, 6100, queued: true));

        var read = (AttackMoveCommand)codec.ReadAs(encoded, 2, 9);

        Assert.That((AttackMoveCommand.Id, read.PlayerId, read.Tick, read.X, read.Y, read.Queued), Is.EqualTo((17, 2, 9, 3400, 6100, true)));
        Assert.That(read.UnitIds, Is.EqualTo(new[] { 5, 7 }));
    }

    [Test]
    public void GWT10_SameSeedAndCommands_SameHashEveryTick()
    {
        List<ulong> Play()
        {
            var sim = Field();
            Unit infantry = Spawn(sim, A, "infantry", 2, 3);
            Spawn(sim, Enemy, "legion_raider", 7, 3);
            sim.Order(Go(sim, Center(12), Center(3), infantry));
            var hashes = new List<ulong>();
            sim.Run(400, s => hashes.Add(s.StateHash));
            return hashes;
        }

        Assert.That(Play(), Is.EqualTo(Play()));
    }

    // ---------- Edge cases ----------

    [Test]
    public void Edge_PointPastTheMapEdge_ClampedAsForMove()
    {
        var sim = Field();
        Unit infantry = Spawn(sim, A, "infantry", 2, 3);

        sim.Order(Go(sim, Cells(100), Cells(-5), infantry));
        sim.Tick();

        Assert.That((infantry.Order.X, infantry.Order.Y), Is.EqualTo((Cells(24) - 1, 0)));
    }

    [Test]
    public void Edge_EnemyOnThePlateau_MeleeGoesUpTheRamp()
    {
        // x ≥ 12 is the plateau of level 1; a ramp of level 0 at (11,9)–(11,10).
        var map = new MapSetup(24, 12).Start(A, 2, 0).Start(B, 18, 0);
        for (int y = 0; y < 12; y++)
            for (int x = 12; x < 24; x++)
                map.Level(x, y, 1);
        map.Level(11, 9, 0, ramp: true).Level(11, 10, 0, ramp: true);
        var sim = new Simulation(Setups.Coop(map));
        Unit infantry = Spawn(sim, A, "infantry", 9, 5);
        Unit raider = Spawn(sim, Enemy, "legion_raider", 13, 5);
        Spawn(sim, A, "scout", 14, 6);   // the team sees the plateau

        sim.Order(Go(sim, Center(10), Center(5), infantry));
        List<HitLanded> hits = Walk(sim, s => raider.IsDead, 60 * Second);

        HitLanded first = HitsBy(hits, infantry).First(h => h.TargetId == raider.Id);
        Assert.That(raider.IsDead, Is.True);
        Assert.That(first, Is.Not.Null);
        MapState m = sim.State.Map;
        Assert.That(m.LevelAt(infantry.CellX, infantry.CellY) == 1 || m.IsRamp(infantry.CellX, infantry.CellY), Is.True);
    }

    [Test]
    public void Edge_CreepsInSightOnTheWay_Attacked()
    {
        var sim = Field();
        Unit infantry = Spawn(sim, A, "infantry", 2, 3);
        Unit creep = Spawn(sim, Setups.Creeps, "creep_small", 7, 4);

        sim.Order(Go(sim, Center(12), Center(3), infantry));
        List<HitLanded> hits = Walk(sim, s => creep.IsDead, 30 * Second);

        Assert.That(HitsBy(hits, infantry).Any(h => h.TargetId == creep.Id), Is.True);
    }

    [Test]
    public void Edge_YellowStarHero_AttackMoves()
    {
        Simulation sim = RepoData.Launch("m2_yellow");
        Unit hero = sim.State.FindHero(1)!;

        sim.Order(new AttackMoveCommand(1, sim.State.Tick, new[] { hero.Id }, hero.X + Cells(3), hero.Y));
        sim.Tick();

        Assert.That(hero.Order.Kind, Is.EqualTo(UnitOrderKind.AttackMove));
    }

    [Test]
    public void Edge_PlayersCaveUnit_AttackMoves()
    {
        var sim = Field();
        Unit beast = Spawn(sim, A, "cave_beast", 2, 3);

        sim.Order(Go(sim, Center(12), Center(3), beast));
        sim.Tick();

        Assert.That(beast.Order.Kind, Is.EqualTo(UnitOrderKind.AttackMove));
    }

    [Test]
    public void Edge_SomeoneElsesUnit_Skipped()
    {
        var sim = Field();
        Unit allyInfantry = Spawn(sim, B, "infantry", 20, 3);

        sim.Order(new AttackMoveCommand(A, sim.State.Tick, new[] { allyInfantry.Id }, Center(12), Center(3)));
        sim.Tick();

        Assert.That(allyInfantry.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
    }

    [Test]
    public void Fail_QueueFull_NotAddedRejected()
    {
        var sim = Field();
        Unit infantry = Spawn(sim, A, "infantry", 2, 3);
        sim.Order(sim.Move(A, Center(20), Center(3), infantry));
        for (int i = 0; i < UnitOrders.MaxQueue; i++)
            sim.Order(new MoveCommand(A, sim.State.Tick, new[] { infantry.Id }, Center(20), Center(4), queued: true));
        sim.Tick();

        sim.Order(Go(sim, Center(5), Center(5), true, infantry));
        var rejected = sim.RunCollecting<CommandRejected>(1);

        Assert.That(rejected.Select(r => r.Reason), Is.EqualTo(new[] { RejectReason.NotAllowed }));
        Assert.That(infantry.OrderQueue, Has.Count.EqualTo(UnitOrders.MaxQueue));
    }
}

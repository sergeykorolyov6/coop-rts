using System.Collections.Generic;

namespace Game.Sim;

// legion-ai.md: once per ai_think_interval the Legion orders workers, rebuilds lost slots,
// produces the next wave, sends waves on their timer, leads them and defends its base.
// It gives itself the same commands a player would (ProduceCommand, BuildCommand), so the
// rules of payment and placement are the players' rules.
internal static class LegionAiSystem
{
    public const string Faction = "black_legion";

    // Waiting units this close to the rally point count as gathered.
    private const int RallySlack = 3 * Grid.MilliPerCell;

    private static readonly List<Unit> Picked = new List<Unit>();

    public static void Tick(SimState state, List<SimEvent> events)
    {
        LegionAi? ai = state.Legion;
        if (ai == null || state.Outcome != MissionOutcome.InProgress)
            return;
        int interval = System.Math.Max(1, state.Rules.AiThinkIntervalTicks);
        if (state.Tick % interval != 0)
            return;

        PruneWaves(state, ai);
        UpdateSlots(state, ai);
        OrderWorkers(state, ai, events);
        Rebuild(state, ai, events);
        ProduceArmy(state, ai, events);
        WarnAndLaunch(state, ai, events);
        LeadWaves(state, ai);
        Defend(state, ai);
        ai.NextWaveTick = WaveTick(state, ai, ai.NextWave);
    }

    // Wave n (1-based): launch tick for this number of players.
    public static int WaveTick(SimState state, LegionAi ai, int n)
    {
        IReadOnlyList<WaveStats> waves = state.Data.Waves;
        if (waves.Count == 0)
            return int.MaxValue;
        long t = n <= waves.Count
            ? waves[n - 1].TimeTicks
            : waves[waves.Count - 1].TimeTicks + (long)(n - waves.Count) * state.Rules.AiWaveRepeatTicks;
        return (int)System.Math.Min(int.MaxValue, t * ai.Stats.WaveTimePercent / 100);
    }

    public static WaveStats WaveRow(SimState state, int n)
    {
        IReadOnlyList<WaveStats> waves = state.Data.Waves;
        return waves[System.Math.Min(n, waves.Count) - 1];
    }

    // How many of a table count the wave really has: scaled by the players, rounded down, at least 1.
    public static int Scaled(LegionAi ai, int count) =>
        count <= 0 ? 0 : System.Math.Max(1, count * ai.Stats.WaveSizePercent / 100);

    private static void PruneWaves(SimState state, LegionAi ai)
    {
        foreach (Wave wave in ai._waves)
            wave._unitIds.RemoveAll(id => !IsAlive(state.FindUnit(id)));
        ai._waves.RemoveAll(w => w._unitIds.Count == 0);
    }

    private static void UpdateSlots(SimState state, LegionAi ai)
    {
        foreach (BaseSlot slot in ai._slots)
        {
            Building? b = state.FindBuilding(slot.BuildingId);
            if (IsAlive(b))
            {
                if (b!.IsComplete)
                    slot.WorkerId = 0;
                continue;
            }

            // A site our worker has just started counts as the slot's building.
            BaseSlotSetup s = slot.Setup;
            Building? there = state.FindBuilding(state.Map.BuildingAt(s.CellX, s.CellY));
            if (IsAlive(there) && there!.OwnerId == ai.PlayerId && there.Stats.Id == s.BuildingTypeId
                && there.CellX == s.CellX && there.CellY == s.CellY)
            {
                slot.BuildingId = there.Id;
                slot.DestroyedTick = Entity.NeverTick;
                continue;
            }
            if (slot.BuildingId != 0 || slot.DestroyedTick == Entity.NeverTick)
            {
                slot.BuildingId = 0;
                slot.DestroyedTick = state.Tick;
            }
        }
    }

    private static void OrderWorkers(SimState state, LegionAi ai, List<SimEvent> events)
    {
        int workers = 0;
        foreach (Unit u in state.Units)
        {
            if (IsAlive(u) && u.OwnerId == ai.PlayerId && u.Worker != null)
                workers++;
        }
        foreach (Building f in Factories(state, ai))
        {
            foreach (string queued in f.Factory!.Queue)
            {
                if (state.Data.Unit(queued).HasTag(UnitTags.Worker))
                    workers++;
            }
        }
        if (workers >= ai.Stats.WorkerLimit)
            return;

        foreach (Building f in Factories(state, ai))
        {
            if (f.Factory!.Queue.Count > 0)
                continue;
            string? worker = WorkerType(state, f);
            if (worker == null)
                continue;
            new ProduceCommand(ai.PlayerId, state.Tick, f.Id, worker).Execute(state, events);
            return;
        }
    }

    private static void Rebuild(SimState state, LegionAi ai, List<SimEvent> events)
    {
        foreach (BaseSlot slot in ai._slots)
        {
            if (slot.BuildingId != 0 || state.Tick - slot.DestroyedTick < ai.Stats.RebuildDelayTicks || EnemyNear(state, ai, slot))
                continue;

            Unit? current = state.FindUnit(slot.WorkerId);
            if (IsAlive(current) && current!.Order.Kind == UnitOrderKind.Build)
                continue;

            Unit? worker = FreeWorker(state, ai);
            if (worker == null)
                return;
            BaseSlotSetup s = slot.Setup;
            new BuildCommand(ai.PlayerId, state.Tick, new[] { worker.Id }, s.BuildingTypeId,
                new[] { new BuildSite(s.CellX, s.CellY, s.Direction) }).Execute(state, events);
            // Rejected (no money, place taken): try again next time.
            slot.WorkerId = worker.Order.Kind == UnitOrderKind.Build ? worker.Id : 0;
        }
    }

    private static void ProduceArmy(SimState state, LegionAi ai, List<SimEvent> events)
    {
        WaveStats row = WaveRow(state, ai.NextWave);
        int army = 0;
        foreach (Unit u in state.Units)
        {
            if (IsFighter(ai, u))
                army++;
        }
        foreach (Building f in Factories(state, ai))
        {
            foreach (string queued in f.Factory!.Queue)
            {
                if (!state.Data.Unit(queued).HasTag(UnitTags.Worker))
                    army++;
            }
        }

        // Still missing for the next wave, in table order.
        var missing = new int[row.Units.Count];
        for (int i = 0; i < row.Units.Count; i++)
            missing[i] = Scaled(ai, row.Units[i].Count) - CountWaiting(state, ai, row.Units[i].UnitId) - CountQueued(state, ai, row.Units[i].UnitId);

        foreach (Building f in Factories(state, ai))
        {
            if (army >= ai.Stats.ArmyLimit)
                return;
            if (f.Factory!.Queue.Count > 0)
                continue;
            for (int i = 0; i < missing.Length; i++)
            {
                if (missing[i] <= 0)
                    continue;
                new ProduceCommand(ai.PlayerId, state.Tick, f.Id, row.Units[i].UnitId).Execute(state, events);
                if (f.Factory.Queue.Count > 0)
                {
                    missing[i]--;
                    army++;
                    break;
                }
            }
        }
    }

    private static void WarnAndLaunch(SimState state, LegionAi ai, List<SimEvent> events)
    {
        if (state.Data.Waves.Count == 0)
            return;
        int n = ai.NextWave;
        int launch = WaveTick(state, ai, n);
        if (!ai.NextWaveWarned && state.Tick >= launch - state.Rules.AiWaveWarningTicks)
        {
            ai.NextWaveWarned = true;
            ai.NextWaveTargetPlayerId = ChooseTargetPlayer(state, n);
            if (ai.NextWaveTargetPlayerId != MatchSetup.NoPlayer)
                events.Add(new WaveIncoming(state.Tick, n, ai.NextWaveTargetPlayerId, launch));
        }
        if (state.Tick < launch)
            return;

        ai.NextWave = n + 1;
        ai.NextWaveWarned = false;
        int player = ai.NextWaveTargetPlayerId;
        if (!IsPlayerAlive(state, player))
            player = ChooseTargetPlayer(state, n);
        if (player == MatchSetup.NoPlayer)
            return;

        WaveStats row = WaveRow(state, n);
        var ids = new List<int>();
        foreach (WaveUnit wu in row.Units)
        {
            int want = Scaled(ai, wu.Count);
            foreach (Unit u in state.Units)
            {
                if (want == 0)
                    break;
                if (IsWaiting(ai, u) && u.Stats.Id == wu.UnitId && !ids.Contains(u.Id))
                {
                    ids.Add(u.Id);
                    want--;
                }
            }
        }
        if (ids.Count == 0)
            return;
        ids.Sort();

        var wave = new Wave(n, row.Target, player, ids);
        wave.TargetId = ChooseTarget(state, wave, Grid.CellCenter(ai.RallyX), Grid.CellCenter(ai.RallyY));
        ai._waves.Add(wave);
        SendWave(state, wave, retarget: true);
        events.Add(new WaveLaunched(state.Tick, n, player, wave.TargetId, ids.Count));
    }

    private static void LeadWaves(SimState state, LegionAi ai)
    {
        foreach (Wave wave in ai._waves)
        {
            bool retarget = !IsAlive(state.FindEntity(wave.TargetId));
            if (retarget)
            {
                Unit lead = state.FindUnit(wave._unitIds[0])!;
                if (!IsPlayerAlive(state, wave.TargetPlayerId))
                    wave.TargetPlayerId = ChooseTargetPlayer(state, wave.Number);
                wave.TargetId = ChooseTarget(state, wave, lead.X, lead.Y);
            }
            SendWave(state, wave, retarget);
        }
    }

    // New target: everyone goes; otherwise only those who stopped (arrived, lost their fight).
    private static void SendWave(SimState state, Wave wave, bool retarget)
    {
        Entity? target = state.FindEntity(wave.TargetId);
        if (!IsAlive(target))
            return;
        (int x, int y) = CenterOf(target!);
        foreach (int id in wave._unitIds)
        {
            Unit u = state.FindUnit(id)!;
            if (retarget || u.Order.Kind == UnitOrderKind.Idle)
                UnitOrders.Replace(u, UnitOrder.AttackMoveTo(x, y));
        }
    }

    private static void Defend(SimState state, LegionAi ai)
    {
        Building? attacked = null;
        foreach (Building b in state.Buildings)
        {
            if (!IsAlive(b) || b.OwnerId != ai.PlayerId || state.Tick - b.LastDamagedTick >= state.Rules.AiDefenseCalmTicks)
                continue;
            if (attacked == null || b.LastDamagedTick > attacked.LastDamagedTick)
                attacked = b;
        }

        int previous = ai.DefendBuildingId;
        ai.DefendBuildingId = attacked?.Id ?? 0;
        // Go for whoever hits the building (a siege unit outranges a defender's sight), or the building itself.
        (int x, int y) = (Grid.CellCenter(ai.RallyX), Grid.CellCenter(ai.RallyY));
        if (attacked != null)
        {
            Entity? attacker = state.FindEntity(attacked.LastAttackerId);
            (x, y) = IsAlive(attacker) ? CenterOf(attacker!) : CenterOf(attacked);
        }
        foreach (Unit u in state.Units)
        {
            if (!IsWaiting(ai, u))
                continue;
            if (attacked != null)
            {
                bool idleFar = u.Order.Kind == UnitOrderKind.Idle && Movement.Distance(u.X, u.Y, x, y) > u.Stats.VisionMilli;
                bool pointMoved = u.Order.Kind == UnitOrderKind.AttackMove && Movement.Distance(u.Order.X, u.Order.Y, x, y) > RallySlack;
                if (ai.DefendBuildingId != previous || idleFar || pointMoved)
                    UnitOrders.Replace(u, UnitOrder.AttackMoveTo(x, y));
            }
            else if (u.Order.Kind == UnitOrderKind.Idle && Movement.Distance(u.X, u.Y, x, y) > RallySlack)
            {
                UnitOrders.Replace(u, UnitOrder.AttackMoveTo(x, y));
            }
        }
    }

    // Wave n goes for team player (n − 1) mod N among those still in the game, by id.
    private static int ChooseTargetPlayer(SimState state, int n)
    {
        var alive = new List<int>();
        foreach (int p in state.TeamPlayerIds)
        {
            if (IsPlayerAlive(state, p))
                alive.Add(p);
        }
        return alive.Count == 0 ? MatchSetup.NoPlayer : alive[(n - 1) % alive.Count];
    }

    // legion-ai.md, "Цель волны" and the fallback order.
    private static int ChooseTarget(SimState state, Wave wave, int fromX, int fromY)
    {
        int player = wave.TargetPlayerId;
        if (wave.Target == WaveStats.TargetEconomy)
        {
            Building? eco = NearestBuilding(state, fromX, fromY, b => b.OwnerId == player && IsEconomy(b));
            if (eco != null)
                return eco.Id;
        }
        Building? hq = state.FindHeadquarters(player);
        if (hq != null)
            return hq.Id;
        Building? any = NearestBuilding(state, fromX, fromY, b => b.OwnerId == player)
            ?? NearestBuilding(state, fromX, fromY, b => IsTeam(state, b.OwnerId));
        if (any != null)
            return any.Id;

        Unit? best = null;
        long bestDist = long.MaxValue;
        foreach (Unit u in state.Units)
        {
            if (!IsAlive(u) || !IsTeam(state, u.OwnerId))
                continue;
            long d = Movement.Distance(fromX, fromY, u.X, u.Y);
            if (d < bestDist)
            {
                best = u;
                bestDist = d;
            }
        }
        return best?.Id ?? 0;
    }

    private static Building? NearestBuilding(SimState state, int x, int y, System.Func<Building, bool> match)
    {
        Building? best = null;
        long bestDist = long.MaxValue;
        foreach (Building b in state.Buildings)
        {
            if (!IsAlive(b) || !match(b))
                continue;
            long d = Combat.DistanceToRectangle(x, y, b);
            if (d < bestDist)
            {
                best = b;
                bestDist = d;
            }
        }
        return best;
    }

    private static bool IsEconomy(Building b) =>
        b.Drill != null || b.Conveyor != null || b.Bridge != null || b.Storage != null;

    private static bool IsTeam(SimState state, int playerId)
    {
        foreach (int p in state.TeamPlayerIds)
        {
            if (p == playerId)
                return true;
        }
        return false;
    }

    private static bool IsPlayerAlive(SimState state, int playerId)
    {
        if (playerId == MatchSetup.NoPlayer)
            return false;
        foreach (Building b in state.Buildings)
        {
            if (IsAlive(b) && b.OwnerId == playerId)
                return true;
        }
        foreach (Unit u in state.Units)
        {
            if (IsAlive(u) && u.OwnerId == playerId)
                return true;
        }
        return false;
    }

    private static bool EnemyNear(SimState state, LegionAi ai, BaseSlot slot)
    {
        BuildingStats stats = state.Data.Building(slot.Setup.BuildingTypeId);
        foreach (Unit u in state.Units)
        {
            if (!IsAlive(u) || !state.Relations.IsEnemy(ai.PlayerId, u.OwnerId))
                continue;
            long d = Movement.DistanceToRectangle(u, slot.Setup.CellX, slot.Setup.CellY, stats.SizeW, stats.SizeH) - u.Radius;
            if (d <= state.Rules.AiRebuildSafeRadiusMilli)
                return true;
        }
        return false;
    }

    // The lowest-id idle Legion worker not already sent to another slot.
    private static Unit? FreeWorker(SimState state, LegionAi ai)
    {
        foreach (Unit u in state.Units)
        {
            if (!IsAlive(u) || u.OwnerId != ai.PlayerId || u.Worker == null || u.Order.Kind != UnitOrderKind.Idle || u.OrderQueue.Count > 0)
                continue;
            return u;
        }
        return null;
    }

    private static List<Building> Factories(SimState state, LegionAi ai)
    {
        var result = new List<Building>();
        foreach (Building b in state.Buildings)
        {
            if (IsAlive(b) && b.OwnerId == ai.PlayerId && b.Factory != null && b.IsComplete)
                result.Add(b);
        }
        return result;
    }

    private static string? WorkerType(SimState state, Building factory)
    {
        foreach (string id in factory.Stats.Produces)
        {
            if (state.Data.Unit(id).HasTag(UnitTags.Worker))
                return id;
        }
        return null;
    }

    private static bool IsFighter(LegionAi ai, Unit u) =>
        IsAlive(u) && u.OwnerId == ai.PlayerId && u.Worker == null;

    // A fighter of the Legion not in any wave: gathers at the rally point or defends the base.
    private static bool IsWaiting(LegionAi ai, Unit u)
    {
        if (!IsFighter(ai, u))
            return false;
        foreach (Wave w in ai._waves)
        {
            if (w._unitIds.Contains(u.Id))
                return false;
        }
        return true;
    }

    private static int CountWaiting(SimState state, LegionAi ai, string unitId)
    {
        int n = 0;
        foreach (Unit u in state.Units)
        {
            if (u.Stats.Id == unitId && IsWaiting(ai, u))
                n++;
        }
        return n;
    }

    private static int CountQueued(SimState state, LegionAi ai, string unitId)
    {
        int n = 0;
        foreach (Building f in Factories(state, ai))
        {
            foreach (string queued in f.Factory!.Queue)
            {
                if (queued == unitId)
                    n++;
            }
        }
        return n;
    }

    private static (int X, int Y) CenterOf(Entity e)
    {
        if (e is Unit u)
            return (u.X, u.Y);
        var b = (Building)e;
        return (Grid.CellToMilli(b.CellX) + Grid.CellToMilli(b.Width) / 2, Grid.CellToMilli(b.CellY) + Grid.CellToMilli(b.Height) / 2);
    }

    private static bool IsAlive(Entity? e) => e != null && !e.IsDead;
}

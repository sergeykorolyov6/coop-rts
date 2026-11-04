using System;
using System.Collections.Generic;

namespace Game.Sim;

// The whole mutable match state. Readable by everyone; mutated only by the
// simulation's tick through internal members.
public sealed class SimState
{
    private readonly List<Player> _players;
    private readonly List<Unit> _units = new List<Unit>();
    private readonly List<Building> _buildings = new List<Building>();
    private readonly List<CreepCamp> _camps = new List<CreepCamp>();
    private readonly List<int> _teamPlayerIds = new List<int>();

    internal SimState(MatchSetup setup)
    {
        Data = setup.Data;
        CreepPlayerId = setup.CreepPlayerId;
        LegionPlayerId = setup.LegionPlayerId;
        NeutralPlayerId = setup.NeutralPlayerId;
        Goal = setup.Goal;
        Map = new MapState(setup.Map);
        Grid = new Grid(setup.MapWidth, setup.MapHeight);
        Rng = new DeterministicRng(setup.Seed);
        ResourceTypeCount = setup.ResourceTypeCount;

        _players = new List<Player>(setup.PlayerIds.Count);
        foreach (int id in setup.PlayerIds)
            _players.Add(new Player(id, setup.ResourceTypeCount));

        Relations = new Relations(setup.PlayerIds);
        foreach (RelationSetup relation in setup.Relations)
            Relations.Set(relation.PlayerA, relation.PlayerB, relation.Relation);

        foreach (PlayerStart start in setup.Map.Starts)
        {
            ApplyStart(start);
            _teamPlayerIds.Add(start.PlayerId);
        }
        _teamPlayerIds.Sort();
        if (LegionPlayerId != MatchSetup.NoPlayer)
            Legion = SpawnLegion(setup.Map);

        // Grey Creatures are enemies of everyone else, whatever the setup says (creep-camp.md, assumption 1);
        // the neutral player is no one's enemy (neutral-buildings.md).
        if (CreepPlayerId != MatchSetup.NoPlayer)
        {
            foreach (int id in setup.PlayerIds)
            {
                if (id != CreepPlayerId && id != NeutralPlayerId)
                    Relations.Set(CreepPlayerId, id, Relation.Enemy);
            }
        }
        if (NeutralPlayerId != MatchSetup.NoPlayer)
        {
            foreach (int id in setup.PlayerIds)
            {
                if (id != NeutralPlayerId)
                    Relations.Set(NeutralPlayerId, id, Relation.Neutral);
            }
        }
        foreach (CreepCampSetup camp in setup.Map.CreepCamps)
            SpawnCamp(camp);
        foreach (NeutralBuildingSetup n in setup.Map.Neutrals)
        {
            Building b = PlaceBuilding(NeutralPlayerId, Data.Building(n.BuildingTypeId), n.CellX, n.CellY, Direction.Right, complete: true);
            b.GuardCampIndex = n.GuardCampIndex;
        }

        Fog = new FogOfWar(_players, Map.Width, Map.Height);
        if (CreepPlayerId != MatchSetup.NoPlayer)
            Fog.SetBlind(CreepPlayerId);
        if (NeutralPlayerId != MatchSetup.NoPlayer)
            Fog.SetBlind(NeutralPlayerId);
        Fog.Update(this);
    }

    public FogOfWar Fog { get; }

    public GameData Data { get; }
    public GameRules Rules => Data.Rules;
    public MapState Map { get; }

    // Builders per plan key in the current tick; rebuilt every tick from hashed worker state.
    internal SortedDictionary<int, int> BuildAssignments { get; } = new SortedDictionary<int, int>();

    // Blows struck in the current tick, applied together after all units acted.
    internal List<PendingHit> PendingHits { get; } = new List<PendingHit>();

    // Path cache: derived from the map, not part of the state hash.
    internal FlowFieldCache Paths { get; } = new FlowFieldCache();

    // Owner of the creeps, MatchSetup.NoPlayer when the map has none.
    public int CreepPlayerId { get; }

    // The Black Legion (legion-ai.md), MatchSetup.NoPlayer and null without one.
    public int LegionPlayerId { get; }
    public LegionAi? Legion { get; }

    // Owner of the neutral buildings (neutral-buildings.md), MatchSetup.NoPlayer without them.
    public int NeutralPlayerId { get; }

    // mission.md.
    public MissionGoal Goal { get; }

    // Players with a start on the map, ascending: the team that wins or loses together (mission.md).
    public IReadOnlyList<int> TeamPlayerIds => _teamPlayerIds;

    public bool IsTeamPlayer(int playerId) => _teamPlayerIds.BinarySearch(playerId) >= 0;

    public MissionOutcome Outcome { get; internal set; }

    // In map order; the index is the camp's id in commands.
    public IReadOnlyList<CreepCamp> CreepCamps => _camps;

    // Both sorted by id; dead entities are removed at the end of the tick.
    public IReadOnlyList<Unit> Units => _units;
    public IReadOnlyList<Building> Buildings => _buildings;

    // Next id for a unit or a building: one counter for both, never reused.
    public int NextEntityId { get; private set; } = 1;

    // The tick that will be executed next.
    public int Tick { get; internal set; }

    public Grid Grid { get; }
    public int ResourceTypeCount { get; }

    // Sorted by id.
    public IReadOnlyList<Player> Players => _players;

    public Relations Relations { get; }

    // Internal: anything outside the sim drawing from it would desync the match.
    internal DeterministicRng Rng { get; }

    public bool HasPlayer(int playerId) => FindPlayer(playerId) != null;

    public Player GetPlayer(int playerId) =>
        FindPlayer(playerId) ?? throw new ArgumentException($"Unknown player {playerId}.", nameof(playerId));

    public Unit? FindUnit(int id) => FindById(_units, id);

    public Building? FindBuilding(int id) => FindById(_buildings, id);

    public Entity? FindEntity(int id) => (Entity?)FindUnit(id) ?? FindBuilding(id);

    // The player's living headquarters (one at a time for a human player), or null.
    public Building? FindHeadquarters(int playerId)
    {
        foreach (Building b in _buildings)
        {
            if (b.OwnerId == playerId && !b.IsDead && b.Stats.Id == BuildingKinds.Headquarters)
                return b;
        }
        return null;
    }

    // builder.md: the player's living builder (at most one), or null.
    public Unit? FindBuilder(int playerId)
    {
        foreach (Unit u in _units)
        {
            if (!u.IsDead && u.OwnerId == playerId && u.IsBuilder)
                return u;
        }
        return null;
    }

    // mission.md: the living boss of a kill_boss mission, or null.
    public Unit? FindBoss()
    {
        foreach (Unit u in _units)
        {
            if (!u.IsDead && u.Stats.HasTag(UnitTags.Boss))
                return u;
        }
        return null;
    }

    // neutral-buildings.md: a neutral building whose guard camp still has creeps cannot be captured.
    public bool IsGuarded(Building b) =>
        b.GuardCampIndex >= 0 && _camps[b.GuardCampIndex].State != CreepCampState.Cleared;

    internal Unit SpawnUnit(int ownerId, UnitStats stats, int x, int y)
    {
        var unit = new Unit(NextEntityId++, ownerId, stats, x, y);
        _units.Add(unit);
        return unit;
    }

    internal Building PlaceBuilding(int ownerId, BuildingStats stats, int cellX, int cellY, Direction direction, bool complete)
    {
        var building = new Building(NextEntityId++, ownerId, stats, cellX, cellY, direction, complete, Rules.ConstructionStartHpPercent);
        _buildings.Add(building);
        Map.Occupy(building);
        BuildingComponents.Attach(this, building);
        return building;
    }

    // Drops dead entities at the end of a tick, keeping id order; frees their cells.
    internal void RemoveDead()
    {
        _units.RemoveAll(u => u.IsDead);
        for (int i = 0; i < _buildings.Count; i++)
        {
            if (_buildings[i].IsDead)
            {
                BuildingComponents.Detach(this, _buildings[i]);
                Map.Free(_buildings[i]);
            }
        }
        _buildings.RemoveAll(b => b.IsDead);
    }

    // Free walkable cells around a footprint, nearest ring first, in a fixed order
    // (bottom row left to right, right column up, top row right to left, left column down).
    internal List<(int X, int Y)> FreeCellsAround(int cellX, int cellY, int width, int height, int count)
    {
        var result = new List<(int X, int Y)>(count);
        for (int ring = 1; result.Count < count && ring <= System.Math.Max(Map.Width, Map.Height); ring++)
        {
            int x0 = cellX - ring, y0 = cellY - ring, x1 = cellX + width - 1 + ring, y1 = cellY + height - 1 + ring;
            for (int x = x0; x <= x1 && result.Count < count; x++) TryAdd(x, y0);
            for (int y = y0 + 1; y <= y1 && result.Count < count; y++) TryAdd(x1, y);
            for (int x = x1 - 1; x >= x0 && result.Count < count; x--) TryAdd(x, y1);
            for (int y = y1 - 1; y > y0 && result.Count < count; y--) TryAdd(x0, y);
        }
        return result;

        void TryAdd(int x, int y)
        {
            if (Map.IsPassable(x, y) && !IsUnitInCell(x, y))
                result.Add((x, y));
        }
    }

    internal bool IsUnitInCell(int x, int y)
    {
        foreach (Unit u in _units)
        {
            if (!u.IsDead && u.CellX == x && u.CellY == y)
                return true;
        }
        return false;
    }

    // construction.md, "Стартовые условия": headquarters, the builder and resources from the rules.
    private void ApplyStart(PlayerStart start)
    {
        Building hq = PlaceBuilding(start.PlayerId, Data.Building(BuildingKinds.Headquarters), start.CellX, start.CellY, Direction.Right, complete: true);
        // builder.md: one builder instead of the M1 workers.
        BuilderSystem.Spawn(this, start.PlayerId, Data.Unit(BuilderSystem.BuilderUnitId), hq);

        Wallet wallet = GetPlayer(start.PlayerId).Wallet;
        wallet.Add(Resources.Primary, Rules.StartPrimary);
        if (ResourceTypeCount > Resources.Secondary)
            wallet.Add(Resources.Secondary, Rules.StartSecondary);
    }

    // legion-ai.md: every slot built at the start, workers next to the first headquarters,
    // money and income by the number of players.
    private LegionAi SpawnLegion(MapSetup map)
    {
        LegionStats stats = Data.LegionFor(_teamPlayerIds.Count);
        var ai = new LegionAi(LegionPlayerId, stats, map.RallyX, map.RallyY);
        Building? hq = null;
        foreach (BaseSlotSetup setup in map.LegionSlots)
        {
            Building b = PlaceBuilding(LegionPlayerId, Data.Building(setup.BuildingTypeId), setup.CellX, setup.CellY, setup.Direction, complete: true);
            ai._slots.Add(new BaseSlot(setup) { BuildingId = b.Id });
            if (hq == null && b.Stats.Id == BuildingKinds.Headquarters)
                hq = b;
        }

        UnitStats? workerStats = null;
        foreach (UnitStats u in Data.Units)
        {
            if (u.Faction == LegionAiSystem.Faction && u.HasTag(UnitTags.Worker))
            {
                workerStats = u;
                break;
            }
        }
        Building home = hq ?? FindBuilding(ai.Slots[0].BuildingId)!;
        if (workerStats != null)
        {
            foreach ((int x, int y) in FreeCellsAround(home.CellX, home.CellY, home.Width, home.Height, stats.StartWorkers))
                SpawnUnit(LegionPlayerId, workerStats, Grid.CellCenter(x), Grid.CellCenter(y));
        }

        Wallet wallet = GetPlayer(LegionPlayerId).Wallet;
        wallet.Add(Resources.Primary, stats.StartPrimary);
        if (ResourceTypeCount > Resources.Secondary)
            wallet.Add(Resources.Secondary, stats.StartSecondary);
        wallet.IncomePercent = stats.IncomePercent;
        return ai;
    }

    private void SpawnCamp(CreepCampSetup setup)
    {
        CreepCampStats stats = Data.CreepCamp(setup.CampTypeId);
        var ids = new List<int>(stats.Creeps.Count);
        for (int i = 0; i < stats.Creeps.Count; i++)
        {
            (int x, int y) = setup.CreepCells[i];
            Unit creep = SpawnUnit(CreepPlayerId, Data.Unit(stats.Creeps[i]), Grid.CellCenter(x), Grid.CellCenter(y));
            creep.CampIndex = _camps.Count;
            creep.HomeX = creep.X;
            creep.HomeY = creep.Y;
            ids.Add(creep.Id);
        }
        _camps.Add(new CreepCamp(_camps.Count, stats, setup.CenterX, setup.CenterY, ids));
    }

    private static T? FindById<T>(List<T> sorted, int id) where T : Entity
    {
        int lo = 0, hi = sorted.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            int midId = sorted[mid].Id;
            if (midId == id)
                return sorted[mid];
            if (midId < id)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return null;
    }

    private Player? FindPlayer(int playerId)
    {
        foreach (Player player in _players)
        {
            if (player.Id == playerId)
                return player;
        }
        return null;
    }
}

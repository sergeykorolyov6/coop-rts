using System;
using System.Collections.Generic;

namespace Game.Sim;

// Balance tables from Assets/Data/*.json (generated from Docs/Design/*.csv).
// The sim takes the json text, not file paths: Presentation reads the files the
// Unity way, tests read them from disk, and Sim stays free of IO.
public sealed class GameData
{
    private readonly List<UnitStats> _units;
    private readonly List<BuildingStats> _buildings;
    private readonly List<CreepCampStats> _creepCamps;
    private readonly List<LegionStats> _legion;
    private readonly List<WaveStats> _waves;
    private readonly List<FactionStats> _factions;
    private readonly List<AbilityStats> _abilities;
    private readonly List<HeroLevelStats> _heroLevels;

    private GameData(List<UnitStats> units, List<BuildingStats> buildings, List<CreepCampStats> creepCamps,
        List<LegionStats> legion, List<WaveStats> waves, List<FactionStats> factions, List<AbilityStats> abilities,
        List<HeroLevelStats> heroLevels, GameRules rules)
    {
        _heroLevels = heroLevels;
        _abilities = abilities;
        _factions = factions;
        _units = units;
        _buildings = buildings;
        _creepCamps = creepCamps;
        _legion = legion;
        _waves = waves;
        Rules = rules;
    }

    public GameRules Rules { get; }

    // No units, buildings or camps and all rules zero: for matches that only test the core loop.
    public static GameData Empty { get; } = Parse("[]", "[]", "[]", GameRules.ZeroJson());

    // Sorted by id (ordinal).
    public IReadOnlyList<UnitStats> Units => _units;
    public IReadOnlyList<BuildingStats> Buildings => _buildings;
    public IReadOnlyList<CreepCampStats> CreepCamps => _creepCamps;

    // legion-ai.md: rows for 1, 2, … players, in order; waves 1, 2, … in order. Empty without a Legion.
    public IReadOnlyList<LegionStats> Legion => _legion;
    public IReadOnlyList<WaveStats> Waves => _waves;

    // factions.csv, sorted by id. Empty in data without players (tests of the core loop).
    public IReadOnlyList<FactionStats> Factions => _factions;

    // abilities.csv, sorted by id.
    public IReadOnlyList<AbilityStats> Abilities => _abilities;

    // hero_levels.csv: level 1, 2, … in order (factions.md, Yellow Star).
    public IReadOnlyList<HeroLevelStats> HeroLevels => _heroLevels;

    // Without the Legion tables: matches with no enemy AI (the M1 prototype, most tests).
    public static GameData Parse(string unitsJson, string buildingsJson, string creepCampsJson, string rulesJson) =>
        Parse(new GameDataSources { Units = unitsJson, Buildings = buildingsJson, CreepCamps = creepCampsJson, Rules = rulesJson });

    public static GameData Parse(GameDataSources sources)
    {
        List<UnitStats> units = ReadTable("units.json", sources.Units, row => new UnitStats(row), u => u.Id);
        List<BuildingStats> buildings = ReadTable("buildings.json", sources.Buildings, row => new BuildingStats(row), b => b.Id);
        List<CreepCampStats> camps = ReadTable("creep_camps.json", sources.CreepCamps, row => new CreepCampStats(row), c => c.Id);
        List<LegionStats> legion = ReadNumbered("legion.json", sources.Legion, row => new LegionStats(row), l => l.Players);
        List<WaveStats> waves = ReadNumbered("waves.json", sources.Waves, row => new WaveStats(row), w => w.Wave);
        List<FactionStats> factions = ReadTable("factions.json", sources.Factions, row => new FactionStats(row), f => f.Id);
        List<AbilityStats> abilities = ReadTable("abilities.json", sources.Abilities, row => new AbilityStats(row), a => a.Id);
        List<HeroLevelStats> heroLevels = ReadNumbered("hero_levels.json", sources.HeroLevels, row => new HeroLevelStats(row), l => l.Level);

        GameRules rules;
        try
        {
            rules = new GameRules(JsonParser.Parse(sources.Rules));
        }
        catch (FormatException e) when (!e.Message.StartsWith("rules.json", StringComparison.Ordinal))
        {
            throw new FormatException($"rules.json: {e.Message}", e);
        }

        var data = new GameData(units, buildings, camps, legion, waves, factions, abilities, heroLevels, rules);
        data.Validate();
        return data;
    }

    public UnitStats Unit(string id) =>
        TryGetUnit(id, out UnitStats? stats) ? stats! : throw new ArgumentException($"Unknown unit \"{id}\".", nameof(id));

    public BuildingStats Building(string id) =>
        TryGetBuilding(id, out BuildingStats? stats) ? stats! : throw new ArgumentException($"Unknown building \"{id}\".", nameof(id));

    public CreepCampStats CreepCamp(string id) =>
        TryGetCreepCamp(id, out CreepCampStats? stats) ? stats! : throw new ArgumentException($"Unknown creep camp \"{id}\".", nameof(id));

    // The row for this many players; past the table's end, its last row.
    public LegionStats LegionFor(int players)
    {
        if (_legion.Count == 0)
            throw new InvalidOperationException("No legion.json rows: this data has no Black Legion.");
        int index = System.Math.Min(System.Math.Max(players, 1), _legion.Count) - 1;
        return _legion[index];
    }

    public FactionStats Faction(string id) =>
        TryGetFaction(id, out FactionStats? stats) ? stats! : throw new ArgumentException($"Unknown faction \"{id}\".", nameof(id));

    public bool TryGetFaction(string id, out FactionStats? stats) => TryFind(_factions, id, f => f.Id, out stats);

    public AbilityStats Ability(string id) =>
        TryGetAbility(id, out AbilityStats? stats) ? stats! : throw new ArgumentException($"Unknown ability \"{id}\".", nameof(id));

    public bool TryGetAbility(string id, out AbilityStats? stats) => TryFind(_abilities, id, a => a.Id, out stats);

    // The first faction of the role (by id), or null when the data has none.
    public FactionStats? DefaultFaction(FactionRole role)
    {
        foreach (FactionStats f in _factions)
        {
            if (f.Role == role)
                return f;
        }
        return null;
    }

    public bool TryGetUnit(string id, out UnitStats? stats) => TryFind(_units, id, u => u.Id, out stats);
    public bool TryGetBuilding(string id, out BuildingStats? stats) => TryFind(_buildings, id, b => b.Id, out stats);
    public bool TryGetCreepCamp(string id, out CreepCampStats? stats) => TryFind(_creepCamps, id, c => c.Id, out stats);

    private static List<T> ReadTable<T>(string fileName, string json, Func<JsonNode, T> create, Func<T, string> idOf)
    {
        JsonNode root;
        try
        {
            root = JsonParser.Parse(json);
        }
        catch (FormatException e)
        {
            throw new FormatException($"{fileName}: {e.Message}", e);
        }
        if (root.Kind != JsonKind.Array)
            throw new FormatException($"{fileName}: expected a list of rows.");

        var rows = new List<T>(root.Items.Count);
        for (int i = 0; i < root.Items.Count; i++)
        {
            try
            {
                rows.Add(create(root.Items[i]));
            }
            catch (FormatException e)
            {
                throw new FormatException($"{fileName}, row {i}: {e.Message}", e);
            }
        }

        rows.Sort((a, b) => string.CompareOrdinal(idOf(a), idOf(b)));
        for (int i = 1; i < rows.Count; i++)
        {
            if (idOf(rows[i]) == idOf(rows[i - 1]))
                throw new FormatException($"{fileName}: duplicate id \"{idOf(rows[i])}\".");
        }
        return rows;
    }

    private static long HalfDiagonalMilli(int w, int h)
    {
        long wm = Grid.CellToMilli(w), hm = Grid.CellToMilli(h);
        return (IntMath.Sqrt(wm * wm + hm * hm) + 1) / 2;
    }

    // Rows numbered 1, 2, … with no gaps (players in legion.json, waves in waves.json).
    private static List<T> ReadNumbered<T>(string fileName, string json, Func<JsonNode, T> create, Func<T, int> numberOf)
    {
        JsonNode root;
        try
        {
            root = JsonParser.Parse(json);
        }
        catch (FormatException e)
        {
            throw new FormatException($"{fileName}: {e.Message}", e);
        }
        if (root.Kind != JsonKind.Array)
            throw new FormatException($"{fileName}: expected a list of rows.");
        var rows = new List<T>(root.Items.Count);
        for (int i = 0; i < root.Items.Count; i++)
        {
            try
            {
                rows.Add(create(root.Items[i]));
            }
            catch (FormatException e)
            {
                throw new FormatException($"{fileName}, row {i}: {e.Message}", e);
            }
        }
        rows.Sort((a, b) => numberOf(a).CompareTo(numberOf(b)));
        for (int i = 0; i < rows.Count; i++)
        {
            if (numberOf(rows[i]) != i + 1)
                throw new FormatException($"{fileName}: rows must be numbered 1, 2, … without gaps; found {numberOf(rows[i])} at place {i + 1}.");
        }
        return rows;
    }

    private static bool TryFind<T>(List<T> sorted, string id, Func<T, string> idOf, out T? found) where T : class
    {
        int lo = 0, hi = sorted.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            int cmp = string.CompareOrdinal(idOf(sorted[mid]), id);
            if (cmp == 0)
            {
                found = sorted[mid];
                return true;
            }
            if (cmp < 0)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        found = null;
        return false;
    }

    // Catches typos in the design tables at load time instead of mid-match.
    private void Validate()
    {
        foreach (UnitStats u in _units)
        {
            if (u.Hp <= 0)
                throw new FormatException($"units.json: \"{u.Id}\" has hp {u.Hp}, must be positive.");
            foreach (string a in u.Abilities)
            {
                if (!TryGetAbility(a, out _))
                    throw new FormatException($"units.json: \"{u.Id}\" has unknown ability \"{a}\".");
            }
            if (u.ProducedAt.Length > 0 && !TryGetBuilding(u.ProducedAt, out _))
                throw new FormatException($"units.json: \"{u.Id}\" is produced at unknown building \"{u.ProducedAt}\".");
        }

        foreach (BuildingStats b in _buildings)
        {
            if (b.Hp <= 0)
                throw new FormatException($"buildings.json: \"{b.Id}\" has hp {b.Hp}, must be positive.");
            if (b.SizeW <= 0 || b.SizeH <= 0)
                throw new FormatException($"buildings.json: \"{b.Id}\" has size {b.SizeW}x{b.SizeH}, must be positive.");
            if (b.MaxBuilders <= 0)
                throw new FormatException($"buildings.json: \"{b.Id}\" has max_builders {b.MaxBuilders}, must be positive.");
            if (b.Ammo > 0 && !b.SuppliedByConveyor)
                throw new FormatException($"buildings.json: \"{b.Id}\" uses ammo but has no stock (supply conveyor).");
            // turret.md: a building that shoots sees everything it can reach from any corner,
            // with a cell to spare for the target's size.
            if (b.Damage > 0)
            {
                long need = b.AttackRangeMilli + HalfDiagonalMilli(b.SizeW, b.SizeH) + Grid.MilliPerCell;
                if (b.VisionMilli < need)
                    throw new FormatException($"buildings.json: \"{b.Id}\" has vision {b.VisionMilli} mc, needs at least {need} (range + half diagonal + 1 cell).");
            }
            foreach (string unit in b.Produces)
            {
                if (!TryGetUnit(unit, out _))
                    throw new FormatException($"buildings.json: \"{b.Id}\" produces unknown unit \"{unit}\".");
            }
        }

        foreach (FactionStats f in _factions)
        {
            if (f.BuilderUnitId.Length > 0 && (!TryGetUnit(f.BuilderUnitId, out UnitStats? builder) || !builder!.HasTag(UnitTags.Builder)))
                throw new FormatException($"factions.json: \"{f.Id}\" has builder \"{f.BuilderUnitId}\" that is not a unit tagged builder.");
            if (f.HeadquartersId.Length > 0 && (!TryGetBuilding(f.HeadquartersId, out BuildingStats? hq) || hq!.Behavior != BuildingBehavior.Headquarters))
                throw new FormatException($"factions.json: \"{f.Id}\" has headquarters \"{f.HeadquartersId}\" that is not a headquarters building.");
            if (f.HeroUnitId.Length > 0 && (!TryGetUnit(f.HeroUnitId, out UnitStats? hero) || !hero!.HasTag(UnitTags.Hero)))
                throw new FormatException($"factions.json: \"{f.Id}\" has hero \"{f.HeroUnitId}\" that is not a unit tagged hero.");
            if (f.HeroUnitId.Length > 0 && _heroLevels.Count == 0)
                throw new FormatException($"factions.json: \"{f.Id}\" has a hero but hero_levels.json is empty.");
            if (f.OnKillAbilityId.Length > 0 && (!TryGetAbility(f.OnKillAbilityId, out AbilityStats? onKill) || onKill!.Kind != AbilityKind.Boost))
                throw new FormatException($"factions.json: \"{f.Id}\" has on-kill ability \"{f.OnKillAbilityId}\" that is not a boost.");
            foreach (string b in f.Buildings)
            {
                if (!TryGetBuilding(b, out _))
                    throw new FormatException($"factions.json: \"{f.Id}\" builds unknown building \"{b}\".");
            }
        }

        int lastTime = -1;
        foreach (WaveStats w in _waves)
        {
            if (w.TimeTicks <= lastTime)
                throw new FormatException($"waves.json: wave {w.Wave} comes no later than the one before.");
            lastTime = w.TimeTicks;
            if (w.Units.Count == 0)
                throw new FormatException($"waves.json: wave {w.Wave} has no units.");
            foreach (WaveUnit u in w.Units)
            {
                if (!TryGetUnit(u.UnitId, out _))
                    throw new FormatException($"waves.json: wave {w.Wave} has unknown unit \"{u.UnitId}\".");
            }
        }
        foreach (LegionStats l in _legion)
        {
            if (l.IncomePercent <= 0 || l.WaveSizePercent <= 0 || l.WaveTimePercent <= 0)
                throw new FormatException($"legion.json: row for {l.Players} players has a zero percentage.");
        }

        foreach (CreepCampStats c in _creepCamps)
        {
            if (c.Creeps.Count == 0)
                throw new FormatException($"creep_camps.json: \"{c.Id}\" has no creeps.");
            foreach (string creep in c.Creeps)
            {
                if (!TryGetUnit(creep, out _))
                    throw new FormatException($"creep_camps.json: \"{c.Id}\" has unknown creep \"{creep}\".");
            }
            if (c.LeashRadiusMilli < c.AggroRadiusMilli)
                throw new FormatException($"creep_camps.json: \"{c.Id}\" leash is shorter than aggro radius.");
        }
    }
}

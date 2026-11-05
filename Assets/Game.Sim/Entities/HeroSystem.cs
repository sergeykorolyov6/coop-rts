using System.Collections.Generic;

namespace Game.Sim;

// factions.md (Yellow Star): the hero gains experience from enemy units dying near it, levels up
// by hero_levels.csv (more HP and damage, abilities open), and lends its aura to own units nearby.
internal static class HeroSystem
{
    // The victim died this tick: every hero within its faction's radius gains its share of experience.
    public static void OnUnitDied(SimState state, Unit victim, List<SimEvent> events)
    {
        foreach (Unit hero in state.Units)
        {
            if (hero.IsDead || !hero.IsHero || !state.Relations.IsEnemy(hero.OwnerId, victim.OwnerId))
                continue;
            Player player = state.GetPlayer(hero.OwnerId);
            FactionStats? f = player.Faction;
            if (f == null || f.HeroXpPercent == 0 || Movement.Distance(hero.X, hero.Y, victim.X, victim.Y) > f.HeroXpRadiusMilli)
                continue;
            player.HeroXp += victim.MaxHp * f.HeroXpPercent / 100;
            IReadOnlyList<HeroLevelStats> levels = state.Data.HeroLevels;
            while (player.HeroLevel < levels.Count && player.HeroXp >= levels[player.HeroLevel].Xp)
            {
                player.HeroLevel++;
                ApplyLevel(state, hero, player.HeroLevel);
                events.Add(new HeroLevelUp(state.Tick, hero.OwnerId, hero.Id, player.HeroLevel));
            }
        }
    }

    // Max HP by the level and the path; the hero keeps the share of HP it had (a full hero stays full).
    public static void ApplyLevel(SimState state, Unit hero, int level)
    {
        HeroPathStats? path = PathOf(state, hero.OwnerId);
        int pct = Level(state, level).HpPercent + (path?.LevelHpPercent ?? 0) * (level - 1);
        int maxHp = hero.Stats.Hp * (100 + pct) / 100;
        int hp = (int)((long)hero.Hp * maxHp / hero.MaxHp);
        hero.MaxHp = maxHp;
        hero.Hp = hp < 1 ? 1 : hp;
    }

    // Percent more damage a unit deals: a hero by its level, own units in its aura by the faction.
    public static int DamageBonusPercent(SimState state, Unit unit)
    {
        Player player = state.GetPlayer(unit.OwnerId);
        FactionStats? f = player.Faction;
        if (f == null || f.HeroUnitId.Length == 0)
            return 0;
        HeroPathStats? path = PathOf(state, unit.OwnerId);
        if (unit.IsHero)
            return Level(state, player.HeroLevel).DamagePercent + (path?.LevelDamagePercent ?? 0) * (player.HeroLevel - 1);
        Unit? hero = state.FindHero(unit.OwnerId);
        return hero != null && Movement.Distance(hero.X, hero.Y, unit.X, unit.Y) <= f.HeroAuraRadiusMilli
            ? f.HeroAuraDamagePercent + (path?.AuraDamagePercent ?? 0)
            : 0;
    }

    // A hero uses only its path's abilities, each from its level; other units use all of theirs.
    public static bool Allows(SimState state, Unit unit, AbilityStats ability)
    {
        if (!unit.IsHero)
            return true;
        HeroPathStats? path = PathOf(state, unit.OwnerId);
        return path != null && Contains(path.Abilities, ability.Id) && state.GetPlayer(unit.OwnerId).HeroLevel >= ability.MinLevel;
    }

    public static HeroPathStats? PathOf(SimState state, int playerId)
    {
        string id = state.GetPlayer(playerId).HeroPathId;
        return id.Length > 0 && state.Data.TryGetHeroPath(id, out HeroPathStats? path) ? path : null;
    }

    private static bool Contains(IReadOnlyList<string> list, string value)
    {
        foreach (string s in list)
        {
            if (s == value)
                return true;
        }
        return false;
    }

    private static HeroLevelStats Level(SimState state, int level) => state.Data.HeroLevels[level - 1];
}

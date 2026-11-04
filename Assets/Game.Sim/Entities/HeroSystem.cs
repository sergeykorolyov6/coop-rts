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

    // Max HP by the level; the hero keeps the share of HP it had (a full hero stays full).
    public static void ApplyLevel(SimState state, Unit hero, int level)
    {
        int maxHp = hero.Stats.Hp * (100 + Level(state, level).HpPercent) / 100;
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
        if (unit.IsHero)
            return Level(state, player.HeroLevel).DamagePercent;
        Unit? hero = state.FindHero(unit.OwnerId);
        return hero != null && Movement.Distance(hero.X, hero.Y, unit.X, unit.Y) <= f.HeroAuraRadiusMilli ? f.HeroAuraDamagePercent : 0;
    }

    // The unit's level for abilities that open later: a hero's, 1 for everyone else.
    public static int LevelOf(SimState state, Unit unit) => unit.IsHero ? state.GetPlayer(unit.OwnerId).HeroLevel : 1;

    private static HeroLevelStats Level(SimState state, int level) => state.Data.HeroLevels[level - 1];
}

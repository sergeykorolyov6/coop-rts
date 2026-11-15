using System.Collections.Generic;
using Game.Sim;

namespace Game.Presentation
{
    // What the UI says about a unit, a building or an ability: the tip of its button and the
    // numbers of the selection card, all from the balance data (units.csv, buildings.csv, abilities.csv).
    public static class EntityTips
    {
        public static HudTip Unit(UnitStats s, string hotkey, Wallet? wallet)
        {
            var tip = new HudTip { Title = s.NameRu, Hotkey = hotkey, Body = s.Description };
            tip.WithPrice(s.CostPrimary, s.CostSecondary,
                wallet != null && wallet.Get(Game.Sim.Resources.Primary) < s.CostPrimary,
                wallet != null && wallet.Secondary < s.CostSecondary,
                DisplayNames.Seconds(s.BuildTimeTicks));
            tip.Stats.AddRange(UnitNumbers(s));
            return tip;
        }

        public static HudTip Building(BuildingStats s, string hotkey, Wallet? wallet)
        {
            var tip = new HudTip { Title = s.NameRu, Hotkey = hotkey, Body = s.Description };
            tip.WithPrice(s.CostPrimary, s.CostSecondary,
                wallet != null && wallet.Get(Game.Sim.Resources.Primary) < s.CostPrimary,
                wallet != null && wallet.Secondary < s.CostSecondary,
                DisplayNames.Seconds(s.BuildTimeTicks));
            tip.Stats.AddRange(BuildingNumbers(s));
            if (s.LinePlacement)
                tip.Note = "Протягивается мышью: зажмите и ведите. R — повернуть.";
            return tip;
        }

        public static HudTip Ability(AbilityStats a, string hotkey)
        {
            var tip = new HudTip { Title = a.NameRu, Hotkey = hotkey, Body = a.Description };
            tip.Stats.Add(("ПЕРЕЗАРЯДКА", DisplayNames.Seconds(a.CooldownTicks)));
            if (a.RangeMilli > 0)
                tip.Stats.Add(("ДАЛЬНОСТЬ", Cells(a.RangeMilli)));
            if (a.RadiusMilli > 0)
                tip.Stats.Add(("РАДИУС", Cells(a.RadiusMilli)));
            if (a.AtPoint)
                tip.Note = "Нажмите, затем ЛКМ по земле — куда. ПКМ или Esc — отмена.";
            return tip;
        }

        public static IEnumerable<(string, string)> UnitNumbers(UnitStats s)
        {
            yield return ("ПРОЧНОСТЬ", s.Hp.ToString());
            yield return ("БРОНЯ", s.Armor.ToString());
            if (s.Damage > 0 || s.DamageVsBuildings > 0)
            {
                yield return ("УРОН", s.DamageVsBuildings != s.Damage ? $"{s.Damage} / {s.DamageVsBuildings} зд." : s.Damage.ToString());
                yield return ("ДАЛЬНОСТЬ", Cells(s.AttackRangeMilli));
            }
            yield return ("СКОРОСТЬ", Cells(s.MoveSpeedMilliPerTick * Simulation.TicksPerSecond) + "/с");
        }

        public static IEnumerable<(string, string)> BuildingNumbers(BuildingStats s)
        {
            yield return ("ПРОЧНОСТЬ", s.Hp.ToString());
            if (s.Armor > 0)
                yield return ("БРОНЯ", s.Armor.ToString());
            if (s.Damage > 0)
            {
                yield return ("УРОН", s.Damage.ToString());
                yield return ("ДАЛЬНОСТЬ", Cells(s.AttackRangeMilli));
            }
            yield return ("РАЗМЕР", $"{s.SizeW}×{s.SizeH}");
        }

        public static string Cells(int milli) => DisplayNames.Number(milli / (double)Grid.MilliPerCell, "0.#") + " кл.";
    }
}

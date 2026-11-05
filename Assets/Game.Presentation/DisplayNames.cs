using Game.Sim;

namespace Game.Presentation
{
    // Russian texts for the UI. Unit, building and camp names are name_ru of Docs/Design/*.csv,
    // read from the loaded balance data (SimRunner sets Data); an unknown id shows as is.
    public static class DisplayNames
    {
        public static GameData? Data { get; set; }

        public static string Building(string id) =>
            Data != null && Data.TryGetBuilding(id, out BuildingStats? stats) ? stats!.NameRu : id;

        public static string Unit(string id) =>
            Data != null && Data.TryGetUnit(id, out UnitStats? stats) ? stats!.NameRu : id;

        // "Чёрный Легион — враг" for someone else's unit or building; "" for the viewer's own.
        public static string Whose(SimState state, int viewerId, int ownerId)
        {
            if (ownerId == viewerId)
                return "";
            string faction = state.GetPlayer(ownerId).Faction?.NameRu ?? "";
            string relation = state.Relations.IsAlly(viewerId, ownerId) ? "союзник" : state.Relations.IsEnemy(viewerId, ownerId) ? "враг" : "нейтрал";
            return faction.Length > 0 ? $"{faction} — {relation}" : relation;
        }

        public static string Clock(int ticks)
        {
            int seconds = System.Math.Max(0, ticks) / Simulation.TicksPerSecond;
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        public static string Camp(string id) =>
            Data != null && Data.TryGetCreepCamp(id, out CreepCampStats? stats) ? stats!.NameRu : id;

        public static string Reject(RejectReason reason, string? buildingId = null)
        {
            switch (reason)
            {
                case RejectReason.NotDiscovered: return "Место не разведано";
                case RejectReason.PlaceTaken: return "Место занято";
                case RejectReason.OreCellTaken: return "Клетку руды уже копают";
                case RejectReason.NotEnoughResources: return "Не хватает ресурсов";
                case RejectReason.NoHeadquarters: return "Нет штаба";
                case RejectReason.OneHeadquartersOnly: return "Штаб уже есть";
                case RejectReason.NothingToRepair: return "Чинить нечего";
                case RejectReason.CannotProduce: return "Нельзя произвести";
                case RejectReason.AbilityNotReady: return "Способность не готова";
                case RejectReason.HeroPathChosen: return "Путь героя уже выбран";
                case RejectReason.FactoryStockLow: return "На складе здания не хватает ресурсов — подведите ленту от бура или склада";
                default:
                    bool drill = buildingId != null && Data != null && Data.TryGetBuilding(buildingId, out BuildingStats? stats) && stats!.Behavior == BuildingBehavior.Drill;
                    return drill ? "Под буром нет руды" : "Сюда нельзя";
            }
        }

        // "units per ticks" as a rate per second: 0,5 ед./с. Invariant culture plus a comma:
        // culture data may be stripped from player builds.
        public static string PerSecond(int units, int ticks) =>
            Number(units * Simulation.TicksPerSecond / (double)ticks, "0.###") + " ед./с";

        public static string Seconds(int ticks) => Number(ticks / (double)Simulation.TicksPerSecond, "0.#") + " с";

        // A decimal with a Russian comma: 1,5.
        public static string Number(double value, string format) =>
            value.ToString(format, System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

        // "24 клетки", "5 клеток", "1 клетка".
        public static string Cells(int n)
        {
            int last2 = n % 100, last = n % 10;
            string word = last2 >= 11 && last2 <= 14 ? "клеток" : last == 1 ? "клетка" : last >= 2 && last <= 4 ? "клетки" : "клеток";
            return $"{n} {word}";
        }
    }
}

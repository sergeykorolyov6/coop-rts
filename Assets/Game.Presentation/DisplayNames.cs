using Game.Sim;

namespace Game.Presentation
{
    // Russian names for the UI. The balance json carries ids only; names follow name_ru in
    // Docs/Design/*.csv and change with them.
    public static class DisplayNames
    {
        public static string Building(string id)
        {
            switch (id)
            {
                case BuildingKinds.Headquarters: return "Штаб";
                case BuildingKinds.Factory: return "Завод";
                case BuildingKinds.Drill: return "Бур";
                case BuildingKinds.Conveyor: return "Лента";
                case BuildingKinds.ConveyorBridge: return "Мост";
                case BuildingKinds.Storage: return "Склад";
                case "turret": return "Турель";
                case "legion_factory": return "Завод Легиона";
                case BuildingKinds.Cave: return "Пещера";
                case BuildingKinds.Lighthouse: return "Маяк";
                default: return id;
            }
        }

        public static string Unit(string id)
        {
            switch (id)
            {
                case "builder": return "Строитель";
                case "repairer": return "Ремонтник";
                case "cave_beast": return "Пещерный зверь";
                case "cave_spitter": return "Плевун";
                case "creep_small": return "Малый крип";
                case "creep_big": return "Большой крип";
                case "creep_boss": return "Вожак Серых Существ";
                case "scout": return "Разведчик";
                case "infantry": return "Пехотинец";
                case "shooter": return "Стрелок";
                case "heavy": return "Тяжёлый";
                case "siege": return "Осадный";
                case "legion_worker": return "Рабочий Легиона";
                case "legion_raider": return "Рейдер Легиона";
                case "legion_gunner": return "Стрелок Легиона";
                case "legion_brute": return "Громила Легиона";
                default: return id;
            }
        }

        public static string Clock(int ticks)
        {
            int seconds = System.Math.Max(0, ticks) / Simulation.TicksPerSecond;
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        // Docs/Design/creep_camps.csv, name_ru.
        public static string Camp(string id)
        {
            switch (id)
            {
                case "guard_camp": return "Стражи";
                case "small_camp": return "Малый лагерь";
                case "cave_guard": return "Стражи пещеры";
                case "big_camp": return "Большой лагерь";
                case "boss_lair": return "Логово вожака";
                default: return id;
            }
        }

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
                case RejectReason.FactoryStockLow: return "На складе здания не хватает ресурсов — подведите ленту от бура или склада";
                default: return buildingId == BuildingKinds.Drill ? "Под буром нет руды" : "Сюда нельзя";
            }
        }

        // "units per ticks" as a rate per second: 0,5 ед./с. Invariant culture plus a comma:
        // culture data may be stripped from player builds.
        public static string PerSecond(int units, int ticks) =>
            (units * Simulation.TicksPerSecond / (double)ticks).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " ед./с";

        public static string Seconds(int ticks) =>
            (ticks / (double)Simulation.TicksPerSecond).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " с";

        // "24 клетки", "5 клеток", "1 клетка".
        public static string Cells(int n)
        {
            int last2 = n % 100, last = n % 10;
            string word = last2 >= 11 && last2 <= 14 ? "клеток" : last == 1 ? "клетка" : last >= 2 && last <= 4 ? "клетки" : "клеток";
            return $"{n} {word}";
        }
    }
}

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
                default: return id;
            }
        }

        public static string Unit(string id)
        {
            switch (id)
            {
                case "worker": return "Рабочий";
                case "scout": return "Разведчик";
                case "infantry": return "Пехотинец";
                case "shooter": return "Стрелок";
                case "heavy": return "Тяжёлый";
                case "siege": return "Осадный";
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
                default: return buildingId == BuildingKinds.Drill ? "Под буром нет руды" : "Сюда нельзя";
            }
        }

        // "units per ticks" as a rate per second: 0,5 ед./с. Invariant culture plus a comma:
        // culture data may be stripped from player builds.
        public static string PerSecond(int units, int ticks) =>
            (units * Simulation.TicksPerSecond / (double)ticks).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " ед./с";

        public static string Seconds(int ticks) =>
            (ticks / (double)Simulation.TicksPerSecond).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " с";
    }
}

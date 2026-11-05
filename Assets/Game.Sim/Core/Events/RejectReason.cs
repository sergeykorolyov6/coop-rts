namespace Game.Sim;

// Why a command did nothing; shown to the player who issued it.
public enum RejectReason : byte
{
    NotAllowed,
    NotDiscovered,
    PlaceTaken,
    OreCellTaken,
    NotEnoughResources,
    NoHeadquarters,
    OneHeadquartersOnly,
    NothingToRepair,
    CannotProduce,

    // factory.md M2: the factory's own stock (filled by belts) cannot pay.
    FactoryStockLow,

    // factions.md: no selected unit has the ability ready.
    AbilityNotReady,

    // factions.md: the hero's path is chosen once a mission.
    HeroPathChosen,
}

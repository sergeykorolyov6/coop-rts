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
}

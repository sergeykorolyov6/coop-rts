namespace Game.Sim;

// A worker handed resources in at a headquarters ("+5" over it, in the carrier's colour).
public sealed class CargoDelivered : SimEvent
{
    public CargoDelivered(int tick, int workerId, int carrierOwnerId, int headquartersId, int creditedPlayerId, int resourceType, int amount)
        : base(tick)
    {
        WorkerId = workerId;
        CarrierOwnerId = carrierOwnerId;
        HeadquartersId = headquartersId;
        CreditedPlayerId = creditedPlayerId;
        ResourceType = resourceType;
        Amount = amount;
    }

    public int WorkerId { get; }
    public int CarrierOwnerId { get; }
    public int HeadquartersId { get; }

    // Owner of the headquarters: the one who gets the resources (answers 2, 20).
    public int CreditedPlayerId { get; }
    public int ResourceType { get; }
    public int Amount { get; }
}

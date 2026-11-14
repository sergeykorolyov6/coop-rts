namespace Game.Sim;

// coop-tools.md: a player gave an ally resources from the wallet.
public sealed class ResourcesGiven : SimEvent
{
    public ResourcesGiven(int tick, int fromPlayerId, int toPlayerId, int resourceType, int amount)
        : base(tick)
    {
        FromPlayerId = fromPlayerId;
        ToPlayerId = toPlayerId;
        ResourceType = resourceType;
        Amount = amount;
    }

    public int FromPlayerId { get; }
    public int ToPlayerId { get; }
    public int ResourceType { get; }
    public int Amount { get; }
}

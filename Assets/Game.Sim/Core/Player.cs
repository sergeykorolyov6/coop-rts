namespace Game.Sim;

public sealed class Player
{
    internal Player(int id, int resourceTypeCount)
    {
        Id = id;
        Wallet = new Wallet(resourceTypeCount);
    }

    public int Id { get; }
    public Wallet Wallet { get; }

    // builder.md: when the dead builder may come back; Entity.NeverTick while it lives
    // (or for players who never had one).
    public int BuilderRespawnTick { get; internal set; } = Entity.NeverTick;
    public bool IsBuilderDead => BuilderRespawnTick != Entity.NeverTick;
}

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
}

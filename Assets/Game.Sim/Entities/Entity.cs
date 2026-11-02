namespace Game.Sim;

// Anything with an owner and hit points. Ids grow monotonically and are never reused:
// "who goes first" ties are always broken by id (CLAUDE.md).
public abstract class Entity
{
    protected Entity(int id, int ownerId, int maxHp, int armor)
    {
        Id = id;
        OwnerId = ownerId;
        MaxHp = maxHp;
        Hp = maxHp;
        Armor = armor;
    }

    public int Id { get; }
    public int OwnerId { get; }
    public int MaxHp { get; }
    public int Hp { get; internal set; }
    public int Armor { get; }

    // Set when HP reaches 0; the entity is removed at the end of the tick.
    public bool IsDead { get; internal set; }
}

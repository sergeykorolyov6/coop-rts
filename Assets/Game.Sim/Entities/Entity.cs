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
    // Changes only when a neutral building is captured (neutral-buildings.md).
    public int OwnerId { get; internal set; }
    // Grows with a hero's level (factions.md); fixed for everything else.
    public int MaxHp { get; internal set; }
    public int Hp { get; internal set; }
    public int Armor { get; }

    // Set when HP reaches 0; the entity is removed at the end of the tick.
    public bool IsDead { get; internal set; }

    // Last blow taken: when and from whom (creeps answer attackers, creep-camp.md).
    public int LastDamagedTick { get; internal set; } = NeverTick;
    public int LastAttackerId { get; internal set; }

    // Tick of the last "attacked" alarm (combat.md, alerts.md: at most once per cooldown).
    internal int LastAlarmTick { get; set; } = NeverTick;

    // Far enough in the past that every "N ticks since" check passes, without overflow.
    internal const int NeverTick = int.MinValue / 2;
}

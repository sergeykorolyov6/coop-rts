namespace Game.Sim;

public sealed class Player
{
    internal Player(int id, int resourceTypeCount, FactionStats? faction)
    {
        Id = id;
        Faction = faction;
        Wallet = new Wallet(resourceTypeCount);
    }

    public int Id { get; }
    public Wallet Wallet { get; }

    // factions.csv; null in matches without faction data (tests of the core loop).
    public FactionStats? Faction { get; }

    // May the player build this at all (construction.md)? Without a faction, nothing.
    public bool CanBuild(string buildingId) => Faction != null && Faction.CanBuild(buildingId);

    // builder.md: when the dead builder may come back; Entity.NeverTick while it lives
    // (or for players who never had one).
    public int BuilderRespawnTick { get; internal set; } = Entity.NeverTick;

    // factions.md (Yellow Star): the hero's return time, experience and level live on the player,
    // so a hero that comes back keeps its level; the climb starts over every mission.
    public int HeroRespawnTick { get; internal set; } = Entity.NeverTick;
    public int HeroXp { get; internal set; }
    public int HeroLevel { get; internal set; } = 1;

    // hero_paths.csv: the path chosen this mission, "" until the player picks one.
    public string HeroPathId { get; internal set; } = "";
}

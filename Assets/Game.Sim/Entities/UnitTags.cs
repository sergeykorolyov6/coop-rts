namespace Game.Sim;

// Tags from the "tags" column of units.csv that the sim reacts to.
public static class UnitTags
{
    public const string Worker = "worker";
    public const string Creep = "creep";

    // builder.md: the player's one builder (also tagged "worker"), and the self-driven repairers.
    public const string Builder = "builder";
    public const string Repairer = "repairer";

    // heights.md: a ranged attacker gets the high-ground range bonus.
    public const string Ranged = "ranged";

    // terrain.md: digs destructible rock (the miner).
    public const string Digger = "digger";

    // mission.md: killing every unit with this tag wins a kill_boss mission.
    public const string Boss = "boss";

    // factions.md (Yellow Star): one per player, comes back after death, gains levels.
    public const string Hero = "hero";

    // attack-move.md: a scout only walks on an attack-move — it is there to look, not to fight.
    public const string Scout = "scout";
}

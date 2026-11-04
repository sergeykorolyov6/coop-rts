namespace Game.Sim;

// Tags from the "tags" column of units.csv that the sim reacts to.
public static class UnitTags
{
    public const string Worker = "worker";
    public const string Creep = "creep";

    // builder.md: the player's one builder (also tagged "worker"), and the self-driven repairers.
    public const string Builder = "builder";
    public const string Repairer = "repairer";

    // mission.md: killing every unit with this tag wins a kill_boss mission.
    public const string Boss = "boss";
}

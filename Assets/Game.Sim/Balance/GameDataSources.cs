namespace Game.Sim;

// The json texts of Assets/Data for GameData.Parse, one property per table. Tables a match does
// not use stay empty lists: a test may load only units, buildings and rules.
public sealed class GameDataSources
{
    public string Units { get; set; } = "[]";
    public string Buildings { get; set; } = "[]";
    public string CreepCamps { get; set; } = "[]";
    public string Rules { get; set; } = GameRules.ZeroJson();
    public string Legion { get; set; } = "[]";
    public string Waves { get; set; } = "[]";
    public string Factions { get; set; } = "[]";
    public string Abilities { get; set; } = "[]";
    public string HeroLevels { get; set; } = "[]";
    public string HeroPaths { get; set; } = "[]";
}

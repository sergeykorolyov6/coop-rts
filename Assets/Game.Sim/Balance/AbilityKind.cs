namespace Game.Sim;

// abilities.csv, "kind" (factions.md).
public enum AbilityKind : byte
{
    // Instantly to a point within range.
    Blink,

    // A timed buff to movement and attack speed, maybe paid with HP.
    Boost,

    // Damage to every enemy within range of the user (Yellow Star's Starfall).
    Nova,

    // A boost to every own unit within range of the user (Yellow Star's Inspire).
    Inspire,
}

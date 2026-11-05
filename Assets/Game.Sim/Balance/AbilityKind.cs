namespace Game.Sim;

// abilities.csv, "kind" (factions.md).
public enum AbilityKind : byte
{
    // To a point within range; with damage, it lands hitting enemies within radius (Charge).
    Blink,

    // A timed buff to the user's movement and attack speed, maybe paid with HP.
    Boost,

    // Damage to every enemy unit within radius of the user or of the point.
    DamageArea,

    // The boost to every own unit within radius of the user or of the point.
    BoostArea,

    // Heal to every own and allied unit within radius of the user or of the point.
    HealArea,

    // Damage to the one enemy unit nearest to the point, within radius of it.
    Strike,
}

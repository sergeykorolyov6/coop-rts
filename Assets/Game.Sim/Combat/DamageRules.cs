namespace Game.Sim;

// combat.md: damage minus the target's armor, never less than 1.
public static class DamageRules
{
    public static int Damage(int damage, int armor) => damage - armor < 1 ? 1 : damage - armor;
}

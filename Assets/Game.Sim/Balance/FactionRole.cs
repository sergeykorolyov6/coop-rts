namespace Game.Sim;

// factions.csv, "role": who plays the faction. A player without a faction given in the setup gets
// the first faction of its role.
public enum FactionRole : byte
{
    Player,
    Legion,
    Creeps,
    Neutral,
}

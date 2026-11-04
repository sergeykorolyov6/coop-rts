namespace Game.Sim;

// mission.md: what wins the mission (missions.csv, "goal").
public enum MissionGoal : byte
{
    // Free play: no victory, no defeat (the M1 prototype).
    None,

    // No Legion headquarters and no Legion factory left.
    DestroyLegion,

    // No living unit tagged "boss" left.
    KillBoss,
}

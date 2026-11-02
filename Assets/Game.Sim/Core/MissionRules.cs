namespace Game.Sim;

// mission.md: victory when the Legion has no headquarters and no factory left (sites count),
// defeat when the team has no headquarters (sites count) and no worker. Victory wins a tie.
internal static class MissionRules
{
    public static void Check(SimState state, System.Collections.Generic.List<SimEvent> events)
    {
        if (state.Outcome != MissionOutcome.InProgress || state.Legion == null)
            return;

        bool legionStands = false, teamStands = false;
        foreach (Building b in state.Buildings)
        {
            if (b.IsDead)
                continue;
            bool key = b.Stats.Id == BuildingKinds.Headquarters || b.Factory != null;
            if (b.OwnerId == state.LegionPlayerId && key)
                legionStands = true;
            else if (b.Stats.Id == BuildingKinds.Headquarters && state.IsTeamPlayer(b.OwnerId))
                teamStands = true;
        }
        if (!teamStands)
        {
            foreach (Unit u in state.Units)
            {
                if (!u.IsDead && u.Worker != null && state.IsTeamPlayer(u.OwnerId))
                {
                    teamStands = true;
                    break;
                }
            }
        }

        if (!legionStands)
            state.Outcome = MissionOutcome.Victory;
        else if (!teamStands)
            state.Outcome = MissionOutcome.Defeat;
        else
            return;
        events.Add(new MissionEnded(state.Tick, state.Outcome));
    }
}

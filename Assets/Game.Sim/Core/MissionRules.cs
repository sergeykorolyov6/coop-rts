namespace Game.Sim;

// mission.md: victory by the mission's goal — destroy_legion: the Legion has no headquarters and
// no factory left (sites count); kill_boss: no living boss is left. Defeat: no team player has a
// finished headquarters and no team builder lives (builder.md). Victory wins a tie.
internal static class MissionRules
{
    public static void Check(SimState state, System.Collections.Generic.List<SimEvent> events)
    {
        if (state.Outcome != MissionOutcome.InProgress || state.Goal == MissionGoal.None)
            return;

        bool goalDone = state.Goal == MissionGoal.DestroyLegion ? !LegionStands(state) : state.FindBoss() == null;
        if (goalDone)
            state.Outcome = MissionOutcome.Victory;
        else if (!TeamStands(state))
            state.Outcome = MissionOutcome.Defeat;
        else
            return;
        events.Add(new MissionEnded(state.Tick, state.Outcome));
    }

    private static bool LegionStands(SimState state)
    {
        foreach (Building b in state.Buildings)
        {
            if (!b.IsDead && b.OwnerId == state.LegionPlayerId && (b.Stats.Id == BuildingKinds.Headquarters || b.Factory != null))
                return true;
        }
        return false;
    }

    private static bool TeamStands(SimState state)
    {
        foreach (Building b in state.Buildings)
        {
            if (!b.IsDead && b.IsComplete && b.Stats.Id == BuildingKinds.Headquarters && state.IsTeamPlayer(b.OwnerId))
                return true;
        }
        foreach (Unit u in state.Units)
        {
            if (!u.IsDead && u.IsBuilder && state.IsTeamPlayer(u.OwnerId))
                return true;
        }
        return false;
    }
}

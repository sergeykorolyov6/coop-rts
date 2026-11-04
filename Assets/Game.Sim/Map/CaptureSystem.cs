using System.Collections.Generic;

namespace Game.Sim;

// neutral-buildings.md: a team player's unit standing by a neutral building with no hostile unit
// around captures it after capture_time (a guarded one only once its camp is cleared); a
// lighthouse shines for whoever stands by it. Runs after movement, before the fog update.
internal static class CaptureSystem
{
    public static void Tick(SimState state, List<SimEvent> events)
    {
        if (state.NeutralPlayerId == MatchSetup.NoPlayer)
            return;
        foreach (Building b in state.Buildings)
        {
            if (b.IsDead)
                continue;
            if (b.Stats.Behavior == BuildingBehavior.Lighthouse)
                TickLighthouse(state, b, events);
            else if (b.OwnerId == state.NeutralPlayerId)
                TickCapture(state, b, events);
        }
    }

    private static void TickLighthouse(SimState state, Building b, List<SimEvent> events)
    {
        Unit? first = FirstTeamUnit(state, b, state.Rules.LighthouseRadiusMilli, 0);
        int lit = first?.OwnerId ?? 0;
        if (lit != 0 && lit != b.LitForPlayerId)
            events.Add(new LighthouseLit(state.Tick, lit, b.Id));
        b.LitForPlayerId = lit;
    }

    private static void TickCapture(SimState state, Building b, List<SimEvent> events)
    {
        int radius = state.Rules.CaptureRadiusMilli;
        if (state.IsGuarded(b))
        {
            Reset(b);
            return;
        }

        // The current capturer keeps going while any of its units stays; otherwise the lowest-id one present starts over.
        int player = b.CapturePlayerId != 0 && FirstTeamUnit(state, b, radius, b.CapturePlayerId) != null
            ? b.CapturePlayerId
            : FirstTeamUnit(state, b, radius, 0)?.OwnerId ?? 0;
        if (player == 0)
        {
            Reset(b);
            return;
        }
        if (player != b.CapturePlayerId)
        {
            b.CapturePlayerId = player;
            b.CaptureProgress = 0;
        }
        if (HostileNear(state, b, radius, player))
            return;

        b.CaptureProgress++;
        if (b.CaptureProgress < state.Rules.CaptureTimeTicks)
            return;
        b.OwnerId = player;
        Reset(b);
        events.Add(new BuildingCaptured(state.Tick, player, b.Id));
    }

    private static void Reset(Building b)
    {
        b.CaptureProgress = 0;
        b.CapturePlayerId = 0;
    }

    private static bool InRadius(Unit u, Building b, int radius) => Combat.EdgeDistance(u, b) <= radius;

    // Lowest-id living unit of a team player (of `onlyPlayer` when it is not 0) within the radius.
    private static Unit? FirstTeamUnit(SimState state, Building b, int radius, int onlyPlayer)
    {
        foreach (Unit u in state.Units)
        {
            if (u.IsDead || !state.IsTeamPlayer(u.OwnerId) || (onlyPlayer != 0 && u.OwnerId != onlyPlayer))
                continue;
            if (InRadius(u, b, radius))
                return u;
        }
        return null;
    }

    private static bool HostileNear(SimState state, Building b, int radius, int player)
    {
        foreach (Unit u in state.Units)
        {
            if (!u.IsDead && state.Relations.IsEnemy(player, u.OwnerId) && InRadius(u, b, radius))
                return true;
        }
        return false;
    }
}

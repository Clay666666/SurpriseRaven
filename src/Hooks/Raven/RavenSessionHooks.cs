namespace SurpriseRaven.Hooks.Raven;

public static class RavenSessionHooks
{
    public static void Enqueue(RavenState state, int lights, string roomKey)
    {
        if (lights is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(lights));
        if (state.HasOpportunity && state.RoomKey != roomKey) Expire(state, "离开房间，剩余机会已放弃。");
        state.RoomKey = roomKey;
        state.PendingLights.Add(lights);
        if (state.PendingLights.Count == 1)
        {
            state.Lights = lights;
            state.Round = 0;
            state.IsActive = false;
            state.Faulted = false;
            state.InFlight = false;
            state.Pending = null;
            state.OpportunityId++;
            state.LastMessage = "获得一次路灯机会。选择遗物投入，或放弃。";
        }
        state.Revision++;
    }

    public static void EndOpportunity(RavenState state, string message)
    {
        if (state.HasOpportunity) state.PendingLights.RemoveAt(0);
        state.BonusCandidates.Clear();
        state.Pending = null;
        state.IsActive = false;
        state.InFlight = false;
        state.Faulted = false;
        state.Round = 0;
        state.Lights = state.HasOpportunity ? state.PendingLights[0] : 0;
        if (state.HasOpportunity) state.OpportunityId++;
        state.LastMessage = message;
        state.Revision++;
    }

    public static void Expire(RavenState state, string message)
    {
        state.PendingLights.Clear();
        EndOpportunity(state, message);
    }

    public static void CompleteRound(RavenState state, int lightRoll) =>
        ApplyRound(state, RavenRulesHooks.NextLights(state.Lights, lightRoll));

    public static void ApplyRound(RavenState state, int nextLights)
    {
        if (nextLights > 6) throw new ArgumentOutOfRangeException(nameof(nextLights));
        if (nextLights >= 0) state.Lights = nextLights;
        state.Round++;
        state.IsActive = true;
        state.InFlight = false;
        state.Revision++;
    }

    // Both a successful pickup and an exhausted bonus choice finish through this boundary.
    public static void CompletePendingRound(RavenState state, RavenPendingRound pending)
    {
        if (pending.RoundAlreadyApplied || IsLegacyAppliedRound(pending))
        {
            pending.RoundAlreadyApplied = true;
            state.IsActive = true;
            state.InFlight = false;
            state.Revision++;
            return;
        }
        ApplyRound(state, pending.NextLights);
        pending.RoundAlreadyApplied = true;
    }

    private static bool IsLegacyAppliedRound(RavenPendingRound pending) =>
        pending.Outcome == RavenOutcome.Bonus && pending.NextLights == -1;

    // 0.2.x saved bonus choices directly; early 0.3.0 migrated them using only NextLights=-1.
    public static bool MigrateLegacy(RavenState state)
    {
        if (state.Pending is { RoundAlreadyApplied: false } pending && IsLegacyAppliedRound(pending))
        {
            pending.RoundAlreadyApplied = true;
            state.Revision++;
            return true;
        }
        if (state.BonusCandidates.Count == 0 || state.Pending is not null) return false;
        state.Pending = new RavenPendingRound
        {
            Outcome = RavenOutcome.Bonus, NextLights = -1, RoundAlreadyApplied = true,
            Candidates = [.. state.BonusCandidates]
        };
        state.BonusCandidates.Clear();
        state.Revision++;
        return true;
    }
}

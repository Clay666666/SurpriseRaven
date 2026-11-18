using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    // Exercises the same completion boundary used after pickup and after candidate exhaustion.
    // Inventory commands remain outside this isolated state test; no run is started.
    private static void TestPendingRoundMigrationCompletion(SerializableRelic candidate)
    {
        foreach (bool alreadyMigrated in new[] { false, true })
        foreach (bool exhausted in new[] { false, true })
        {
            var state = RestoreOldPendingRound(candidate, alreadyMigrated);
            Check(state.Round == 5 && state.Lights == 3, "Old save fixture changed before migration.");
            Check(RavenSessionHooks.MigrateLegacy(state), "Old saved bonus was not normalized.");
            Check(state.Pending is { RoundAlreadyApplied: true } && state.BonusCandidates.Count == 0,
                "Migration did not record the already applied round.");
            state = RoundTripPendingState(state);
            Check(!RavenSessionHooks.MigrateLegacy(state), "Restored normalized round migrated twice.");
            var pending = state.Pending!;
            Check(pending.Candidates.Count == 2, "Migration changed the bonus candidate count.");
            if (exhausted) pending.Candidates.Clear();
            state.Pending = null; // Both production paths clear the pending choice before completion.
            state.InFlight = !exhausted;
            long revision = state.Revision;
            RavenSessionHooks.CompletePendingRound(state, pending);
            Check(state.Round == 5 && state.Lights == 3 && state.IsActive && !state.InFlight,
                $"Legacy {(alreadyMigrated ? "0.3.0" : "0.2.x")} {(exhausted ? "exhaustion" : "pickup")} advanced twice.");
            Check(state.Revision > revision && state.Pending is null, "Completion retained the pending choice.");
            state = RoundTripPendingState(state);
            Check(!RavenSessionHooks.MigrateLegacy(state) && state.Round == 5 && state.Lights == 3,
                "Saving a completed legacy round revived or advanced it.");

            var next = new RavenPendingRound { Outcome = RavenOutcome.Bonus, NextLights = 4 };
            RavenSessionHooks.CompletePendingRound(state, next);
            RavenSessionHooks.CompletePendingRound(state, next);
            Check(state.Round == 6 && state.Lights == 4 && next.RoundAlreadyApplied,
                "The next normal round did not advance exactly once.");
        }

        // A settlement may reach this boundary before lifecycle normalization on load.
        var early = RestoreOldPendingRound(candidate, true);
        var earlyPending = early.Pending!;
        Check(!earlyPending.RoundAlreadyApplied, "Old 0.3.0 fixture unexpectedly contains the new marker.");
        early.Pending = null;
        RavenSessionHooks.CompletePendingRound(early, earlyPending);
        Check(early.Round == 5 && early.Lights == 3 && earlyPending.RoundAlreadyApplied,
            "An unnormalized 0.3.0 saved sentinel advanced the round.");

        foreach (bool alreadyMigrated in new[] { false, true })
        {
            var abandoned = RestoreOldPendingRound(candidate, alreadyMigrated);
            RavenSessionHooks.MigrateLegacy(abandoned);
            RavenSessionHooks.EndOpportunity(abandoned, "放弃旧奖励");
            abandoned = RoundTripPendingState(abandoned);
            Check(!abandoned.HasOpportunity && abandoned.Pending is null && abandoned.BonusCandidates.Count == 0
                  && !RavenSessionHooks.MigrateLegacy(abandoned), "An abandoned legacy choice was restored.");
        }

        // Missing new fields in an ordinary 0.3.0 save must not suppress its first completion.
        var fresh = RestoreOldPendingRound(candidate, true);
        fresh.Pending!.NextLights = 4;
        Check(!RavenSessionHooks.MigrateLegacy(fresh), "A normal pending round was treated as legacy.");
        var freshPending = fresh.Pending!;
        fresh.Pending = null;
        RavenSessionHooks.CompletePendingRound(fresh, freshPending);
        Check(fresh.Round == 6 && fresh.Lights == 4 && freshPending.RoundAlreadyApplied,
            "An ordinary saved round failed to advance.");
    }

    private static RavenState RestoreOldPendingRound(SerializableRelic candidate, bool alreadyMigrated)
    {
        var state = new RavenState
        {
            PendingLights = [2], RoomKey = "legacy-room", Round = 5, Lights = 3, IsActive = true,
            BonusCandidates = alreadyMigrated ? [] : [candidate, candidate],
            Pending = alreadyMigrated ? new RavenPendingRound
            {
                Outcome = RavenOutcome.Bonus, NextLights = -1, Candidates = [candidate, candidate]
            } : null
        };
        JsonNode json = JsonSerializer.SerializeToNode(state, SaveOptions)!;
        if (json["Pending"] is JsonObject pending) pending.Remove("RoundAlreadyApplied");
        return json.Deserialize<RavenState>(SaveOptions) ?? throw new InvalidOperationException("Old state did not restore.");
    }

    private static RavenState RoundTripPendingState(RavenState state) =>
        JsonSerializer.Deserialize<RavenState>(JsonSerializer.Serialize(state, SaveOptions), SaveOptions)
        ?? throw new InvalidOperationException("Pending state did not restore.");
}

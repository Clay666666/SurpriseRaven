using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestSacrificeTransactions() => TestSacrificeTransactionsAsync().GetAwaiter().GetResult();

    private static async Task TestSacrificeTransactionsAsync()
    {
        foreach (var outcome in new[] { RavenOutcome.Upgrade, RavenOutcome.Swap, RavenOutcome.Downgrade })
        {
            var (state, pending) = TransactionFixture(outcome);
            int removed = 0, awarded = 0, writes = 0;
            var removal = new TaskCompletionSource();
            var commit = RavenTransactionHooks.CommitAsync(state, pending, () =>
            {
                Check(writes > 0 && state.InFlight && pending.StakeSnapshot is not null,
                    "Removal began before the locked snapshot was marked for saving.");
                removed++;
                return removal.Task;
            }, () => writes++);
            Check(state.InFlight && !commit.IsCompleted && removed == 1 && awarded == 0,
                "Drop did not consume exactly once before waiting for its callback.");
            Check(!await RavenTransactionHooks.CommitAsync(state, pending, () => { removed++; return Task.CompletedTask; }, () => { }),
                "Repeated drop entered an in-flight submission.");
            Check(await RavenTransactionHooks.CollectAsync(state, pending, 0, _ => true,
                _ => { awarded++; return Task.CompletedTask; }, () => { }) == RavenClaimResult.Rejected,
                "Uncommitted output was claimable.");
            var interrupted = RoundTripPendingState(state);
            Check(RavenTransactionHooks.RecoverInterrupted(interrupted) && interrupted.Faulted,
                "Interrupted removal was not stopped after restore.");
            removal.SetResult();
            Check(await commit && pending.StakeConsumed && !state.InFlight && awarded == 0
                && RavenTransactionHooks.IsReady(pending), "Committed result was not left waiting for a click.");
            var restored = RoundTripPendingState(state);
            Check(restored.Pending is { StakeConsumed: true, SettlementVersion: 2, StakeSnapshot: not null }
                && !RavenTransactionHooks.RecoverInterrupted(restored), "Committed stake metadata failed to round trip.");
            Check(RelicModel.FromSerializable(restored.Pending!.StakeSnapshot!).FloorAddedToDeck == 7,
                "The display snapshot lost original relic properties.");
            var pickup = new TaskCompletionSource();
            var collect = RavenTransactionHooks.CollectAsync(state, pending, 0, _ => true, _ =>
            {
                Check(state.Pending is null && state.InFlight, "Pickup left a retryable pending reward.");
                awarded++;
                return pickup.Task;
            }, () => writes++);
            Check(!collect.IsCompleted && awarded == 1 && removed == 1, "Click did not acquire once without removing again.");
            Check(await RavenTransactionHooks.CollectAsync(state, pending, 0, _ => true,
                _ => { awarded++; return Task.CompletedTask; }, () => { }) == RavenClaimResult.Rejected,
                "Double click reentered an in-flight pickup.");
            interrupted = RoundTripPendingState(state);
            Check(RavenTransactionHooks.RecoverInterrupted(interrupted) && interrupted.Pending is null,
                "Interrupted pickup revived a claimable candidate.");
            pickup.SetResult();
            Check(await collect == RavenClaimResult.Claimed && state.Round == 1 && state.Lights == 3
                && !state.InFlight && removed == 1 && awarded == 1, "Pickup did not finish the round exactly once.");
            Check(await RavenTransactionHooks.CollectAsync(state, pending, 0, _ => true,
                _ => { awarded++; return Task.CompletedTask; }, () => { }) == RavenClaimResult.Rejected,
                "Settled reward was claimable again.");
            pending.StakeIndex = 99;
            Check(RavenTransactionHooks.RewardIndex(pending, 0) == 0 && RavenTransactionHooks.RewardIndex(pending, 2) == 2,
                "Delayed pickup index was not clamped to the remaining inventory.");
        }

        foreach (var outcome in new[] { RavenOutcome.Keep, RavenOutcome.Bonus })
        {
            var (state, pending) = TransactionFixture(outcome);
            int removed = 0, awarded = 0, selected = -1;
            Check(await RavenTransactionHooks.CommitAsync(state, pending, () => { removed++; return Task.CompletedTask; }, () => { }),
                "Retained-stake submission failed.");
            Check(removed == 0 && !pending.StakeConsumed, "Keep or Bonus removed its original relic.");
            if (outcome == RavenOutcome.Bonus)
                Check(RavenTransactionHooks.CollectOriginal(state, pending, true, () => { }) == RavenClaimResult.Claimed,
                    "Original confirmation failed before selecting a bonus.");
            int choice = outcome == RavenOutcome.Bonus ? 2 : 0;
            Check(await RavenTransactionHooks.CollectAsync(state, pending, choice, _ => true,
                index => { awarded++; selected = index; return Task.CompletedTask; }, () => { }) == RavenClaimResult.Claimed,
                "Retained-stake result could not be confirmed.");
            Check(removed == 0 && awarded == (outcome == RavenOutcome.Bonus ? 1 : 0) && state.Pending is null
                && state.Round == 1, "Keep re-obtained its original or Bonus granted multiple candidates.");
            Check(outcome != RavenOutcome.Bonus || selected == 2 && RavenTransactionHooks.RewardIndex(pending, 2) == -1,
                "Bonus did not honor the selected candidate and append position.");
        }

        foreach (bool hasNext in new[] { false, true })
        {
            var (state, pending) = TransactionFixture(RavenOutcome.Swallow);
            if (hasNext) RavenSessionHooks.Enqueue(state, 4, state.RoomKey);
            int removed = 0;
            Check(await RavenTransactionHooks.CommitAsync(state, pending, () => { removed++; return Task.CompletedTask; }, () => { }),
                "Swallow did not commit.");
            Check(RavenTransactionHooks.CompleteSwallow(state) && !RavenTransactionHooks.CompleteSwallow(state)
                && removed == 1 && state.HasOpportunity == hasNext && state.EndedBySwallow == !hasNext
                && state.Lights == (hasNext ? 4 : 0), "Swallow consumed twice or did not advance to the expected opportunity.");
        }
        foreach (var outcome in new[] { RavenOutcome.Swap, RavenOutcome.Keep, RavenOutcome.Bonus })
        {
            var (state, pending) = TransactionFixture(outcome);
            int removed = 0;
            await RavenTransactionHooks.CommitAsync(state, pending, () => { removed++; return Task.CompletedTask; }, () => { });
            await RavenTransactionHooks.AbandonAsync(state, () => { removed++; return Task.CompletedTask; }, () => { });
            Check(!state.HasOpportunity && state.Pending is null && removed == (outcome is RavenOutcome.Swap or RavenOutcome.Bonus ? 1 : 0),
                "Leaving did not discard the pending output while preserving the committed stake disposition.");
        }
    }

    private static (RavenState State, RavenPendingRound Pending) TransactionFixture(RavenOutcome outcome)
    {
        var source = ModelDb.Relic<Strawberry>().ToMutable();
        source.FloorAddedToDeck = 7;
        var snapshot = source.ToSerializable();
        var pending = new RavenPendingRound
        {
            SettlementVersion = RavenTransactionHooks.CurrentVersion, StakeSnapshot = snapshot,
            StakeId = "fixture-stake", StakeTitle = "测试遗物", StakeIndex = 2, Outcome = outcome, NextLights = 3,
            Candidates = outcome == RavenOutcome.Bonus ? [snapshot, snapshot, snapshot]
                : RavenServiceHooks.IsReplacement(outcome) ? [snapshot] : []
        };
        return (new RavenState { PendingLights = [2], Lights = 2, RoomKey = "fixture" }, pending);
    }
}

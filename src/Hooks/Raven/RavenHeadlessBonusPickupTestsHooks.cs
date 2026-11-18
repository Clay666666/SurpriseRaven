using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestBonusIndependentPickup() => TestBonusIndependentPickupAsync().GetAwaiter().GetResult();

    private static async Task TestBonusIndependentPickupAsync()
    {
        foreach (bool originalFirst in new[] { true, false })
        {
            var (state, pending) = TransactionFixture(RavenOutcome.Bonus);
            pending.AncientId = "fixture-ancient";
            int removed = 0, obtained = 0;
            await RavenTransactionHooks.CommitAsync(state, pending, () => { removed++; return Task.CompletedTask; }, () => { });
            if (originalFirst)
                Check(RavenTransactionHooks.CollectOriginal(state, pending, true, () => { }) == RavenClaimResult.Claimed,
                    "Original-first claim failed.");
            else
                Check(await RavenTransactionHooks.CollectAsync(state, pending, 1, _ => true,
                    _ => { obtained++; return Task.CompletedTask; }, () => { }) == RavenClaimResult.Claimed,
                    "Bonus-first claim failed.");
            Check(state.Pending == pending && state.Round == 0 && removed == 0
                && pending.BonusResolved == !originalFirst && pending.Candidates.Count == (originalFirst ? 3 : 0),
                "First pickup resolved or removed the independent group.");
            state = RoundTripPendingState(state);
            pending = state.Pending!;
            Check(pending.AncientId == "fixture-ancient" && pending.OriginalState ==
                (originalFirst ? RavenOriginalState.Returned : RavenOriginalState.Pending), "Partial bonus pickup did not restore.");
            if (originalFirst)
                Check(await RavenTransactionHooks.CollectAsync(state, pending, 2, _ => true,
                    _ => { obtained++; return Task.CompletedTask; }, () => { }) == RavenClaimResult.Claimed,
                    "Remaining bonus group could not be claimed.");
            else
                Check(RavenTransactionHooks.CollectOriginal(state, pending, true, () => { }) == RavenClaimResult.Claimed,
                    "Remaining original could not be claimed.");
            Check(state.Pending is null && state.Round == 1 && state.Lights == 3 && obtained == 1 && removed == 0,
                "Two independent pickups did not finish exactly one round.");
            Check(RavenTransactionHooks.CollectOriginal(state, pending, true, () => { }) == RavenClaimResult.Rejected
                && await RavenTransactionHooks.CollectAsync(state, pending, 0, _ => true,
                    _ => { obtained++; return Task.CompletedTask; }, () => { }) == RavenClaimResult.Rejected && obtained == 1,
                "Duplicate pickup repeated a completed group.");
        }
        {
            var (state, pending) = TransactionFixture(RavenOutcome.Bonus);
            await RavenTransactionHooks.CommitAsync(state, pending, () => Task.CompletedTask, () => { });
            var effect = new TaskCompletionSource();
            int obtained = 0;
            var claim = RavenTransactionHooks.CollectAsync(state, pending, 0, _ => true,
                _ => { obtained++; return effect.Task; }, () => { });
            Check(state.InFlight && pending.BonusResolved && pending.Candidates.Count == 0 && state.Pending == pending,
                "Bonus selection was not committed before its pickup effect.");
            Check(RavenTransactionHooks.CollectOriginal(state, pending, true, () => { }) == RavenClaimResult.Rejected,
                "Original claim raced a pending pickup effect.");
            var interrupted = RoundTripPendingState(state);
            Check(RavenTransactionHooks.RecoverInterrupted(interrupted) && interrupted.Pending is { BonusResolved: true },
                "Interrupted bonus acquisition lost its committed group marker.");
            effect.SetException(new InvalidOperationException("fixture bonus pickup effect failed after obtaining"));
            Check(await claim == RavenClaimResult.Faulted && state.Faulted && obtained == 1
                && pending.OriginalState == RavenOriginalState.Pending, "Failed pickup retried or returned the original automatically.");
        }
        {
            var (state, pending) = TransactionFixture(RavenOutcome.Bonus);
            await RavenTransactionHooks.CommitAsync(state, pending, () => Task.CompletedTask, () => { });
            for (int i = 0; i < 3; i++)
                await RavenTransactionHooks.CollectAsync(state, pending, 0, _ => false,
                    _ => throw new InvalidOperationException("Invalid candidate must not be obtained."), () => { });
            Check(pending.BonusResolved && state.Pending == pending && state.Round == 0,
                "Exhausted bonus candidates discarded the unclaimed original.");
            Check(!string.IsNullOrEmpty(pending.Notice) && RoundTripPendingState(state).Pending!.Notice == pending.Notice,
                "Candidate invalidation notice did not survive rebuilding or restoring the view.");
            Check(RavenTransactionHooks.CollectOriginal(state, pending, false, () => { }) == RavenClaimResult.Invalidated
                && state.Pending is null && state.Round == 1, "Missing original did not finish the already exhausted round.");
        }
        foreach (var outcome in Enum.GetValues<RavenOutcome>())
        {
            var (state, pending) = TransactionFixture(outcome);
            pending.SettlementVersion = 1;
            pending.StakeConsumed = RavenTransactionHooks.ConsumesStake(outcome);
            state.Pending = pending;
            var candidates = pending.Candidates.ToArray();
            Check(await RavenTransactionHooks.CommitAsync(state, pending,
                () => throw new InvalidOperationException("V1 migration must not remove again."), () => { }, legacy: true),
                "Committed v1 result did not migrate.");
            Check(pending.SettlementVersion == 2 && pending.Candidates.SequenceEqual(candidates)
                && (outcome != RavenOutcome.Bonus || pending.OriginalState == RavenOriginalState.Returned),
                "V1 migration changed locked candidates or made its held original pending.");
        }
    }

    private static void TestOriginalIdentity()
    {
        var original = ModelDb.Relic<Strawberry>().ToMutable();
        var duplicate = ModelDb.Relic<Strawberry>().ToMutable();
        List<RelicModel> inventory = [original, duplicate];
        var identity = new RavenOriginalIdentityHooks(original);
        original.FloorAddedToDeck = 17;
        Check(ReferenceEquals(identity.Find(inventory), original) && identity.Find(inventory)!.FloorAddedToDeck == 17,
            "Original identity lost a mutation or cloned the relic.");
        inventory.Remove(original);
        Check(identity.Find(inventory) is null, "Removed original rebound to another relic with the same ID.");
        var pending = new RavenPendingRound { StakeId = original.Id.ToString(), StakeIndex = 99 };
        Check(ReferenceEquals(RavenOriginalIdentityHooks.Restore(inventory, pending).Find(inventory), duplicate),
            "Unique saved original could not be located after an index change.");
        inventory.Add(original);
        Check(RavenOriginalIdentityHooks.Restore(inventory, pending).Ambiguous,
            "Ambiguous saved identity selected an arbitrary duplicate.");
        pending.StakeIndex = 1;
        Check(ReferenceEquals(RavenOriginalIdentityHooks.Restore(inventory, pending).Find(inventory), original),
            "Exact persisted index and ID did not restore its instance.");
    }
}

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestBonusAbandonment() => TestBonusAbandonmentAsync().GetAwaiter().GetResult();

    private static async Task TestBonusAbandonmentAsync()
    {
        foreach (bool originalTaken in new[] { false, true })
        foreach (bool bonusTaken in new[] { false, true })
        {
            var (state, pending) = TransactionFixture(RavenOutcome.Bonus);
            int obtained = 0, removed = 0;
            await RavenTransactionHooks.CommitAsync(state, pending, () => Task.CompletedTask, () => { });
            if (originalTaken) RavenTransactionHooks.CollectOriginal(state, pending, true, () => { });
            if (bonusTaken) await RavenTransactionHooks.CollectAsync(state, pending, 0, _ => true,
                _ => { obtained++; return Task.CompletedTask; }, () => { });
            Check(await RavenTransactionHooks.AbandonAsync(state, () => { removed++; return Task.CompletedTask; }, () => { })
                && removed == (originalTaken ? 0 : 1) && obtained == (bonusTaken ? 1 : 0)
                && state.Pending is null && !state.HasOpportunity,
                "Exit removed a claimed original, kept an unclaimed original, or auto-granted a bonus.");
        }
        {
            var (state, pending) = TransactionFixture(RavenOutcome.Bonus);
            state.RoomKey = "old-room";
            await RavenTransactionHooks.CommitAsync(state, pending, () => Task.CompletedTask, () => { });
            RavenOpportunityQueueHooks.Enqueue(state, 5, "new-room");
            Check(state.Pending == pending && state.RoomKey == "old-room" && state.DeferredOpportunities.Count == 1,
                "Acquisition after a room change cleared the unclaimed original.");
            state = RoundTripPendingState(state);
            pending = state.Pending!;
            Check(state.DeferredOpportunities.Single() == new RavenDeferredOpportunity(5, "new-room"),
                "Deferred acquisition did not survive a save.");
            int removed = 0;
            var removal = new TaskCompletionSource();
            var expire = RavenTransactionHooks.AbandonAsync(state, () =>
            {
                Check(state.InFlight && pending.OriginalState == RavenOriginalState.ForfeitStarted,
                    "Original removal was not marked before awaiting its callback.");
                removed++;
                return removal.Task;
            }, () => { }, expire: true);
            RavenOpportunityQueueHooks.Enqueue(state, 1, "new-room");
            Check(!await RavenTransactionHooks.AbandonAsync(state, () => { removed++; return Task.CompletedTask; }, () => { })
                && !RavenOpportunityQueueHooks.Flush(state, "new-room") && removed == 1,
                "Concurrent exit or acquisition bypassed a pending original removal.");
            var interrupted = RoundTripPendingState(state);
            Check(RavenTransactionHooks.RecoverInterrupted(interrupted) && interrupted.Pending!.OriginalState == RavenOriginalState.ForfeitStarted,
                "Interrupted original forfeiture lost its no-retry marker.");
            removal.SetResult();
            Check(await expire && !state.HasOpportunity && state.Pending is null && removed == 1,
                "Room expiry did not finish the single original removal.");
            Check(RavenOpportunityQueueHooks.Flush(state, "new-room") && state.PendingLights.SequenceEqual(new[] { 5, 1 })
                && state.RoomKey == "new-room" && state.DeferredOpportunities.Count == 0,
                "Deferred acquisitions were lost or reordered after forfeiture.");
        }
        {
            var (state, pending) = TransactionFixture(RavenOutcome.Bonus);
            int removed = 0;
            await RavenTransactionHooks.CommitAsync(state, pending, () => Task.CompletedTask, () => { });
            Task RemoveAndFail() { removed++; return Task.FromException(new InvalidOperationException("fixture original forfeiture failed after removal")); }
            Check(!await RavenTransactionHooks.AbandonAsync(state, RemoveAndFail, () => { }) && state.Faulted
                && !state.InFlight && state.Pending == pending && pending.OriginalState == RavenOriginalState.ForfeitStarted,
                "Failed original removal lost its diagnostic pending state.");
            Check(await RavenTransactionHooks.AbandonAsync(state, RemoveAndFail, () => { }) && removed == 1,
                "Explicit exit after a failed forfeiture repeated the removal command.");
        }
        {
            var (state, pending) = TransactionFixture(RavenOutcome.Bonus);
            int removed = 0;
            await RavenTransactionHooks.CommitAsync(state, pending, () => Task.CompletedTask, () => { });
            RavenTransactionHooks.CollectOriginal(state, pending, false, () => { });
            Check(await RavenTransactionHooks.AbandonAsync(state, () => { removed++; return Task.CompletedTask; }, () => { })
                && removed == 0, "Already removed original caused another removal at exit.");
            RavenOpportunityQueueHooks.Enqueue(state, 3, "current-room");
            Check(state.PendingLights.SequenceEqual(new[] { 3 }), "Ordinary acquisition no longer enqueued immediately.");
            RavenOpportunityQueueHooks.Enqueue(state, 2, "expired-room");
            RavenSessionHooks.Expire(state, "fixture cleanup");
            Check(RavenOpportunityQueueHooks.Flush(state, "current-room") && !state.HasOpportunity,
                "A deferred acquisition was carried across another room boundary.");
        }
    }
}

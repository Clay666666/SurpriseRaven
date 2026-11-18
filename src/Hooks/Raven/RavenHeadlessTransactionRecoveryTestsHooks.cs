namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestSacrificeRecovery() => TestSacrificeRecoveryAsync().GetAwaiter().GetResult();

    private static async Task TestSacrificeRecoveryAsync()
    {
        foreach (var outcome in Enum.GetValues<RavenOutcome>())
        {
            var (state, pending) = TransactionFixture(outcome);
            pending.SettlementVersion = 0;
            state.Pending = pending;
            state.Round = 5;
            if (outcome == RavenOutcome.Bonus)
            {
                pending.StakeSnapshot = null; // 0.2.x bonus choices did not remember a source.
                pending.NextLights = -1;
                pending.RoundAlreadyApplied = true;
            }
            int removed = 0, awarded = 0;
            Check(!RavenTransactionHooks.IsReady(pending), "Legacy pending was exposed before normalization.");
            Check(await RavenTransactionHooks.CommitAsync(state, pending,
                () => { removed++; return Task.CompletedTask; }, () => { }, legacy: true), "Legacy result failed to normalize.");
            Check(removed == (RavenTransactionHooks.ConsumesStake(outcome) ? 1 : 0)
                && RavenTransactionHooks.IsReady(pending), "Legacy result used the wrong removal policy.");
            Check(await RavenTransactionHooks.CommitAsync(state, pending,
                () => { removed++; return Task.CompletedTask; }, () => { }, legacy: true)
                && removed == (RavenTransactionHooks.ConsumesStake(outcome) ? 1 : 0), "Normalized legacy result removed again.");
            var restored = RoundTripPendingState(state);
            Check(restored.Pending!.SettlementVersion == 2 && restored.Pending.StakeConsumed == pending.StakeConsumed,
                "Normalized legacy result lost its consumption marker.");
            if (outcome == RavenOutcome.Swallow)
                Check(RavenTransactionHooks.CompleteSwallow(restored), "Restored consumed swallow did not finish.");
            else
            {
                Check(await RavenTransactionHooks.CollectAsync(restored, restored.Pending, 0, _ => true,
                    _ => { awarded++; return Task.CompletedTask; }, () => { }) == RavenClaimResult.Claimed,
                    "Restored normalized candidate could not be collected.");
                Check(restored.Round == (outcome == RavenOutcome.Bonus ? 5 : 6)
                    && restored.Lights == (outcome == RavenOutcome.Bonus ? 2 : 3), "Legacy completion advanced twice or rerolled lights.");
            }
        }
        foreach (int invalid in new[] { 0, 1, 2, 3 })
        {
            var (state, pending) = TransactionFixture(RavenOutcome.Swap);
            state.Pending = pending;
            pending.SettlementVersion = invalid;
            if (invalid == 0) pending.StakeSnapshot = null;
            int removed = 0;
            Check(!await RavenTransactionHooks.CommitAsync(state, pending,
                () => { removed++; return Task.CompletedTask; }, () => { }, legacy: true)
                && state.Faulted && removed == 0, "Missing source, uncommitted current state or future protocol was replayed.");
        }
        foreach (var outcome in new[] { RavenOutcome.Swap, RavenOutcome.Bonus })
        {
            var (state, pending) = TransactionFixture(outcome);
            int awarded = 0;
            await RavenTransactionHooks.CommitAsync(state, pending, () => Task.CompletedTask, () => { });
            if (outcome == RavenOutcome.Bonus) RavenTransactionHooks.CollectOriginal(state, pending, true, () => { });
            int candidates = pending.Candidates.Count;
            for (int i = 0; i < candidates; i++)
            {
                Check(await RavenTransactionHooks.CollectAsync(state, pending, 0, _ => false,
                    _ => { awarded++; return Task.CompletedTask; }, () => { }) == RavenClaimResult.Invalidated,
                    "Invalid candidate was not discarded.");
                Check(awarded == 0 && state.Round == (i == candidates - 1 ? 1 : 0),
                    "Candidate invalidation granted an output or advanced early.");
            }
            Check(state.Pending is null && pending.StakeConsumed == (outcome == RavenOutcome.Swap),
                "Exhausted candidates refunded a stake or retained pending state.");
        }
        {
            var (state, pending) = TransactionFixture(RavenOutcome.Downgrade);
            int removed = 0, awarded = 0;
            Task RemoveAndFail() { removed++; return Task.FromException(new InvalidOperationException("fixture removed before callback failure")); }
            Check(!await RavenTransactionHooks.CommitAsync(state, pending, RemoveAndFail, () => { })
                && state.Faulted && !state.InFlight && ReferenceEquals(state.Pending, pending),
                "Failed removal did not retain a stopped diagnostic result.");
            Check(!await RavenTransactionHooks.CommitAsync(state, pending, RemoveAndFail, () => { }) && removed == 1,
                "Failed removal was retried.");
            Check(await RavenTransactionHooks.CollectAsync(state, pending, 0, _ => true,
                _ => { awarded++; return Task.CompletedTask; }, () => { }) == RavenClaimResult.Rejected && awarded == 0,
                "Failed sacrifice still allowed a pickup.");
        }
        {
            var (state, pending) = TransactionFixture(RavenOutcome.Upgrade);
            int awarded = 0;
            await RavenTransactionHooks.CommitAsync(state, pending, () => Task.CompletedTask, () => { });
            Task ObtainAndFail(int _) { awarded++; return Task.FromException(new InvalidOperationException("fixture obtained before callback failure")); }
            Check(await RavenTransactionHooks.CollectAsync(state, pending, 0, _ => true, ObtainAndFail, () => { }) == RavenClaimResult.Faulted
                && state.Faulted && !state.InFlight && state.Pending is null && state.Round == 0,
                "Failed pickup retained a retryable candidate or falsely completed its round.");
            Check(await RavenTransactionHooks.CollectAsync(state, pending, 0, _ => true, ObtainAndFail, () => { }) == RavenClaimResult.Rejected
                && awarded == 1, "Failed pickup granted twice.");
        }
        {
            var (state, pending) = TransactionFixture(RavenOutcome.Swap);
            int removed = 0, awarded = 0;
            await RavenTransactionHooks.CommitAsync(state, pending, () => { removed++; return Task.CompletedTask; }, () => { });
            Check(await RavenTransactionHooks.CollectAsync(state, pending, 0,
                _ => throw new InvalidOperationException("fixture candidate validation failure"),
                _ => { awarded++; return Task.CompletedTask; }, () => { }) == RavenClaimResult.Faulted
                && state.Faulted && !state.InFlight && ReferenceEquals(state.Pending, pending) && pending.StakeConsumed
                && removed == 1 && awarded == 0, "Validation exception refunded, regranted, or lost the stopped result.");
        }
    }
}

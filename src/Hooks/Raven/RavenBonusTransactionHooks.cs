namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenTransactionHooks
{
    internal static RavenClaimResult CollectOriginal(RavenState state, RavenPendingRound pending,
        bool originalAvailable, Action persist)
    {
        if (state.Faulted || state.InFlight || !state.HasOpportunity || !ReferenceEquals(state.Pending, pending)
            || !IsReady(pending) || pending.Outcome != RavenOutcome.Bonus || pending.OriginalState != RavenOriginalState.Pending)
            return RavenClaimResult.Rejected;
        pending.OriginalState = originalAvailable ? RavenOriginalState.Returned : RavenOriginalState.Missing;
        state.Revision++;
        state.LastMessage = originalAvailable ? "原遗物已取回；剩余额外奖励可继续领取。"
            : "原遗物已被其他效果移除，无法返还；不会重新生成原件。";
        pending.Notice = originalAvailable ? null : state.LastMessage;
        CompleteBonusIfReady(state, pending);
        persist();
        return originalAvailable ? RavenClaimResult.Claimed : RavenClaimResult.Invalidated;
    }

    private static async Task<RavenClaimResult> CollectBonusAsync(RavenState state, RavenPendingRound pending,
        int index, Func<int, bool> canClaim, Func<int, Task> obtain, Action persist)
    {
        if (pending.BonusResolved || index < 0 || index >= pending.Candidates.Count) return RavenClaimResult.Rejected;
        bool valid;
        try { valid = canClaim(index); }
        catch (Exception error) { Fault(state, error); persist(); return RavenClaimResult.Faulted; }
        if (!valid)
        {
            pending.Candidates.RemoveAt(index);
            pending.BonusResolved = pending.Candidates.Count == 0;
            state.Revision++;
            state.LastMessage = "该额外候选已不满足获得条件，已移除。" + (pending.OriginalState == RavenOriginalState.Pending
                ? "原件仍可单独取回。" : pending.OriginalState == RavenOriginalState.Missing ? "原件已被其他效果移除。" : "原件已取回。");
            pending.Notice = state.LastMessage;
            CompleteBonusIfReady(state, pending);
            persist();
            return RavenClaimResult.Invalidated;
        }
        bool success = await RunAsync(state, persist, async () =>
        {
            // Keep the other group, but commit this selection before Obtain can invoke effects.
            pending.BonusResolved = true;
            pending.Candidates.Clear();
            if (pending.OriginalState != RavenOriginalState.Missing) pending.Notice = null;
            persist();
            await obtain(index);
            CompleteBonusIfReady(state, pending);
        });
        return success ? RavenClaimResult.Claimed : RavenClaimResult.Faulted;
    }

    private static void CompleteBonusIfReady(RavenState state, RavenPendingRound pending)
    {
        if (pending.Outcome != RavenOutcome.Bonus || !pending.BonusResolved || pending.OriginalState is not
            (RavenOriginalState.Returned or RavenOriginalState.Missing or RavenOriginalState.Forfeited)
            || !ReferenceEquals(state.Pending, pending)) return;
        state.Pending = null;
        RavenSessionHooks.CompletePendingRound(state, pending);
    }

    internal static async Task<bool> AbandonAsync(RavenState state, Func<Task> forfeitOriginal, Action persist, bool expire = false)
    {
        if (state.InFlight) return false;
        if (state.Pending is { Outcome: RavenOutcome.Bonus, OriginalState: RavenOriginalState.Pending } pending)
        {
            bool forfeited = await RunAsync(state, persist, async () =>
            {
                pending.OriginalState = RavenOriginalState.ForfeitStarted;
                persist();
                await forfeitOriginal();
                pending.OriginalState = RavenOriginalState.Forfeited;
                pending.StakeConsumed = true;
            });
            if (!forfeited) return false;
        }
        // Explicitly abandoning an interrupted forfeiture never attempts the command again.
        if (expire) RavenSessionHooks.Expire(state, "离开房间，未领取遗物与剩余机会已放弃。");
        else Abandon(state);
        persist();
        return true;
    }
}

namespace SurpriseRaven.Hooks.Raven;

internal enum RavenClaimResult { Rejected, Claimed, Invalidated, Faulted }

// Inventory commands are injected so the production state boundary can be tested without a run.
internal static partial class RavenTransactionHooks
{
    internal const int CurrentVersion = 2;
    internal static bool ConsumesStake(RavenOutcome outcome) => outcome is RavenOutcome.Swallow
        or RavenOutcome.Upgrade or RavenOutcome.Swap or RavenOutcome.Downgrade;
    internal static bool IsReady(RavenPendingRound pending) => pending.SettlementVersion == CurrentVersion
        && (pending.Outcome == RavenOutcome.Bonus ? pending.OriginalState != RavenOriginalState.None
            && (pending.OriginalState != RavenOriginalState.Pending || pending.StakeSnapshot is not null) : pending.StakeSnapshot is not null)
        && (!ConsumesStake(pending.Outcome) || pending.StakeConsumed);
    internal static int RewardIndex(RavenPendingRound pending, int relicCount) => pending.Outcome == RavenOutcome.Bonus
        ? -1 : Math.Clamp(pending.StakeIndex, 0, relicCount);

    internal static async Task<bool> CommitAsync(RavenState state, RavenPendingRound pending,
        Func<Task> remove, Action persist, bool legacy = false)
    {
        if (state.Faulted || state.InFlight || !state.HasOpportunity) return false;
        if (legacy ? !ReferenceEquals(state.Pending, pending) : state.Pending is not null) return false;
        if (legacy && IsReady(pending)) return true;
        if ((legacy ? pending.SettlementVersion is not (0 or 1) : pending.SettlementVersion != CurrentVersion)
            || (pending.StakeSnapshot is null && (pending.Outcome != RavenOutcome.Bonus || !legacy))
            || (legacy && pending.SettlementVersion == 1 && ConsumesStake(pending.Outcome) ? !pending.StakeConsumed : pending.StakeConsumed))
        {
            Fault(state, new InvalidOperationException("Incomplete or unsupported sacrifice state; refusing another removal."));
            persist();
            return false;
        }
        state.Pending = pending;
        return await RunAsync(state, persist, async () =>
        {
            if (pending.Outcome == RavenOutcome.Bonus)
            {
                pending.OriginalState = legacy ? RavenOriginalState.Returned : RavenOriginalState.Pending;
                pending.BonusResolved = pending.Candidates.Count == 0;
            }
            pending.SettlementVersion = CurrentVersion;
            persist();
            if (ConsumesStake(pending.Outcome) && !pending.StakeConsumed)
            {
                await remove();
                pending.StakeConsumed = true;
            }
            if (pending.Shortage is not null) state.BlockedStakeIds.Add(pending.StakeId);
            state.LastMessage = pending.Notice ?? $"渡鸦叼走了 {pending.StakeTitle}……等它回来。";
            CompleteBonusIfReady(state, pending);
        });
    }

    internal static async Task<RavenClaimResult> CollectAsync(RavenState state, RavenPendingRound pending,
        int index, Func<int, bool> canClaim, Func<int, Task> obtain, Action persist)
    {
        if (state.Faulted || state.InFlight || !state.HasOpportunity || !ReferenceEquals(state.Pending, pending) || !IsReady(pending)
            || pending.Outcome == RavenOutcome.Swallow) return RavenClaimResult.Rejected;
        if (pending.Outcome == RavenOutcome.Bonus)
            return await CollectBonusAsync(state, pending, index, canClaim, obtain, persist);
        bool keep = pending.Outcome == RavenOutcome.Keep;
        if (index < 0 || (keep ? index != 0 : index >= pending.Candidates.Count)
            || (pending.Outcome != RavenOutcome.Bonus && index != 0)) return RavenClaimResult.Rejected;
        bool valid;
        try { valid = keep || canClaim(index); }
        catch (Exception error)
        {
            Fault(state, error);
            persist();
            return RavenClaimResult.Faulted;
        }
        if (!valid)
        {
            pending.Candidates.RemoveAt(index);
            state.LastMessage = "奖励已不满足获得条件，本轮产物作废；投入的遗物已消耗。";
            if (pending.Candidates.Count == 0)
            {
                state.Pending = null;
                RavenSessionHooks.CompletePendingRound(state, pending);
            }
            else state.Revision++;
            persist();
            return RavenClaimResult.Invalidated;
        }
        bool success = await RunAsync(state, persist, async () =>
        {
            // Obtain mutates inventory before awaiting pickup effects. Never leave a retryable reward.
            state.Pending = null;
            persist();
            if (!keep) await obtain(index);
            RavenSessionHooks.CompletePendingRound(state, pending);
        });
        return success ? RavenClaimResult.Claimed : RavenClaimResult.Faulted;
    }

    internal static bool CompleteSwallow(RavenState state)
    {
        if (state.Faulted || state.InFlight || !state.HasOpportunity || state.Pending is not { Outcome: RavenOutcome.Swallow } pending
            || !IsReady(pending)) return false;
        RavenSessionHooks.EndOpportunity(state, SwallowMessage(pending));
        state.EndedBySwallow = !state.HasOpportunity;
        return true;
    }

    internal static string SwallowMessage(RavenPendingRound pending) =>
        $"渡鸦带走了 {pending.StakeTitle}。";

    internal static void Abandon(RavenState state)
    {
        if (state.InFlight) return;
        if (state.Pending is { Outcome: RavenOutcome.Bonus, OriginalState: RavenOriginalState.Pending })
            throw new InvalidOperationException("An unclaimed original requires asynchronous forfeiture.");
        string message = state.Faulted ? "已退出；结算曾中断，实际遗物状态保留，请检查日志。"
            : state.Pending?.OriginalState == RavenOriginalState.Missing
            ? "已退出；未领取奖励已放弃，原件已被其他效果移除。" : state.Pending?.StakeConsumed == true
            ? "已退出；未领取的产物已放弃，投入的遗物不返还。"
            : "已退出；未领取的奖励已放弃，原遗物保留。";
        RavenSessionHooks.EndOpportunity(state, message);
    }

    internal static bool RecoverInterrupted(RavenState state)
    {
        if (!state.InFlight) return false;
        state.InFlight = false;
        Fault(state, new InvalidOperationException("Saved sacrifice or pickup was in flight; actual inventory retained, no replay."));
        return true;
    }

    internal static void Fault(RavenState state, Exception error)
    {
        state.Faulted = true;
        state.Revision++;
        state.LastMessage = "结算异常，当前渡鸦路灯已停止。请退出并保留日志。";
        Logger.Log($"[Raven] Settlement failed; actual inventory retained, no retry or refund: {error}");
    }

    private static async Task<bool> RunAsync(RavenState state, Action persist, Func<Task> operation)
    {
        state.InFlight = true;
        state.IsActive = true;
        state.Revision++;
        try
        {
            persist();
            await operation();
            return true;
        }
        catch (Exception error) { Fault(state, error); return false; }
        finally
        {
            state.InFlight = false;
            persist();
        }
    }
}

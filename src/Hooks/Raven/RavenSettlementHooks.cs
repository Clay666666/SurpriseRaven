using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenServiceHooks
{
    public static async Task CollectAsync(Player player, int index, long expectedRevision = -1)
        => await CollectAsync(player, new RavenPickupRequest(RavenPickupKind.Candidate, index), expectedRevision);

    public static async Task CollectAsync(Player player, RavenPickupRequest request, long expectedRevision = -1)
    {
        var state = RavenStorageHooks.Get(player);
        if (state.Pending is not { } pending || !CanStart(player, state, expectedRevision)
            || !RavenTransactionHooks.IsReady(pending)) return;
        if (HasUnavailableOffer(player, pending))
        {
            await ReturnUnavailableOfferAsync(player, state, pending);
            return;
        }
        RavenReplayLogHooks.BeforeClaim(player, pending, request);
        await RunOperationAsync(player, state, async () =>
        {
            if (request.Kind == RavenPickupKind.Original)
            {
                var identity = OriginalIdentity(player, pending);
                if (identity.Ambiguous) throw new InvalidOperationException("Ambiguous original; refusing to return another relic.");
                var originalResult = RavenTransactionHooks.CollectOriginal(state, pending,
                    identity.Find(player.Relics) is not null, () => PersistCurrent(player));
                Logger.Log($"[Raven] Original pickup: {pending.StakeId}, result={originalResult}; no Obtain.");
                return;
            }
            if (request.Kind != RavenPickupKind.Candidate) return;
            RelicModel? candidate = null;
            var result = await RavenTransactionHooks.CollectAsync(state, pending, request.CandidateIndex, selected =>
            {
                candidate = RelicModel.FromSerializable(pending.Candidates[selected]);
                return RavenPoolHooks.CanClaim(player, candidate);
            }, async _ =>
            {
                int position = RavenTransactionHooks.RewardIndex(pending, player.Relics.Count());
                await RelicCmd.Obtain(candidate!, player, position);
            }, () => PersistCurrent(player));
            if (result == RavenClaimResult.Claimed)
            {
                state.LastMessage = pending.Shortage is not null ? pending.Notice!
                    : pending.Outcome == RavenOutcome.Keep ? "原件保留，可以继续投入或退出。"
                    : pending.Outcome == RavenOutcome.Bonus
                        ? $"额外获得 {candidate!.Title.GetFormattedText()}。" + (pending.OriginalState == RavenOriginalState.Pending ? "原件仍待取回。" : "原件已处理。")
                        : $"{pending.StakeTitle} → {candidate!.Title.GetFormattedText()}。可以继续投入或退出。";
                Logger.Log($"[Raven] Claimed {pending.Outcome}: {candidate?.Id.ToString() ?? pending.StakeId}.");
            }
            else if (result == RavenClaimResult.Invalidated)
                Logger.Log($"[Raven] Candidate invalidated without reroll: outcome={pending.Outcome} stake={pending.StakeId}.");
        });
    }

    public static Task ResolveSwallowAsync(Player player, long expectedRevision = -1)
    {
        var state = RavenStorageHooks.Get(player);
        if (!CanStart(player, state, expectedRevision)) return Task.CompletedTask;
        if (RavenTransactionHooks.CompleteSwallow(state))
        {
            Logger.Log("[Raven] Committed swallow animation completed; no second removal.");
            SaveAndNotify(player);
        }
        return Task.CompletedTask;
    }

    internal static bool IsReplacement(RavenOutcome outcome) =>
        outcome is RavenOutcome.Upgrade or RavenOutcome.Swap or RavenOutcome.Downgrade;

    internal static RelicModel? ResolveStake(Player player, RavenPendingRound pending)
    {
        var atIndex = pending.StakeIndex >= 0 ? player.Relics.ElementAtOrDefault(pending.StakeIndex) : null;
        if (atIndex is not null && atIndex.Id.ToString() == pending.StakeId && !atIndex.HasBeenRemovedFromState)
            return atIndex;
        return player.Relics.FirstOrDefault(r => r.Id.ToString() == pending.StakeId && !r.HasBeenRemovedFromState);
    }

    private static bool CanStart(Player player, RavenState state, long revision) =>
        CanInteract(player) && state.HasOpportunity && !state.Faulted && !state.InFlight
        && state.RoomKey == RoomKey(player) && (revision < 0 || revision == state.Revision);

    private static async Task RunOperationAsync(Player player, RavenState state, Func<Task> operation,
        [System.Runtime.CompilerServices.CallerMemberName] string action = "")
    {
        var lifetime = _lifetime;
        lifetime.Busy = true;
        lifetime.ExitRequested = false;
        lifetime.Suppression.Value++;
        try { await operation(); }
        catch (Exception error) { RavenTransactionHooks.Fault(state, error); }
        finally
        {
            try
            {
                if (ReferenceEquals(lifetime, _lifetime))
                {
                    ReconcileOriginal(player, state);
                    if (lifetime.ExitRequested) await AbandonCoreAsync(player, state, state.RoomKey != RoomKey(player));
                }
            }
            finally
            {
                lifetime.Suppression.Value = Math.Max(0, lifetime.Suppression.Value - 1);
                lifetime.Busy = false;
                if (ReferenceEquals(lifetime, _lifetime)) SaveAndNotify(player, action);
            }
        }
    }
}

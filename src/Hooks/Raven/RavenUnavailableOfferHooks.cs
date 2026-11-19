using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenTransactionHooks
{
    internal static void MarkShortage(RavenPendingRound pending, RavenOutcome rolled, bool returned)
    {
        string reason = rolled switch
        {
            RavenOutcome.Bonus => "三选一候选不足三件",
            RavenOutcome.Upgrade => "升档无可用遗物",
            RavenOutcome.Downgrade => "降档无可用遗物",
            _ => "平换无可用遗物"
        };
        pending.Shortage = rolled;
        pending.Outcome = RavenOutcome.Keep;
        pending.OriginalState = RavenOriginalState.None;
        pending.Candidates.Clear();
        pending.Notice = $"{reason}，{pending.StakeTitle}{(returned ? "已返还" : "已被其他效果移除")}，本局不可再次投入。";
    }

    internal static Task<bool> ReturnUnavailableAsync(RavenState state, RavenPendingRound pending, Func<bool> returnOriginal,
        Action persist) => RunAsync(state, persist, () =>
    {
        var rolled = pending.Outcome;
        bool returned = returnOriginal();
        MarkShortage(pending, rolled, returned);
        state.BlockedStakeIds.Add(pending.StakeId);
        state.LastMessage = pending.Notice!;
        state.Pending = null;
        RavenSessionHooks.CompletePendingRound(state, pending);
        return Task.CompletedTask;
    });
}

public static partial class RavenServiceHooks
{
    internal static bool HasUnavailableOffer(Player player, RavenPendingRound pending)
    {
        if (!RavenTransactionHooks.IsReady(pending)) return false;
        int expected = pending.Outcome == RavenOutcome.Bonus && !pending.BonusResolved ? 3
            : IsReplacement(pending.Outcome) ? 1 : 0;
        return expected > 0 && (pending.Candidates.Count != expected
            || pending.Candidates.Select(saved => saved.Id).Distinct().Count() != expected
            || pending.Candidates.Any(saved => !RavenPoolHooks.CanClaim(player, RelicModel.FromSerializable(saved))));
    }

    private static Task ReturnUnavailableOfferAsync(Player player, RavenState state, RavenPendingRound pending)
    {
        if (!ReferenceEquals(state.Pending, pending) || !CanStart(player, state, state.Revision)
            || !HasUnavailableOffer(player, pending)) return Task.CompletedTask;
        return RunOperationAsync(player, state, async () =>
        {
            if (await RavenTransactionHooks.ReturnUnavailableAsync(state, pending, () => RestorePaidOriginal(player, pending),
                () => PersistCurrent(player)))
            {
                Logger.Log($"[Raven] Locked offer unavailable: stake={pending.StakeId} rolled={pending.Shortage} target={pending.Target}.");
                RavenOverlayHooks.ShowShortagePop();
            }
        });
    }

    internal static bool RestorePaidOriginal(Player player, RavenPendingRound pending)
    {
        if (player.Relics.Any(relic => relic.Id.ToString() == pending.StakeId)) return true;
        if (!pending.StakeConsumed || pending.StakeSnapshot is null) return false;
        var original = RelicModel.FromSerializable(pending.StakeSnapshot);
        player.AddRelicInternal(original, Math.Clamp(pending.StakeIndex, 0, player.Relics.Count()));
        return true;
    }
}

using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;

namespace SurpriseRaven.Hooks.Raven;

public sealed record RavenRoundPlan(RavenOutcome Outcome, RelicModel? Stake, IReadOnlyList<RelicModel> Rewards,
    RelicRarity Target, int LightsBefore, int LightsAfter, RelicModel? OriginalReturn = null)
{
    public RavenOriginalState OriginalState { get; init; }
    public string? Notice { get; init; }

    internal RavenOutcome DisplayOutcome =>
        Outcome == RavenOutcome.Downgrade && Stake is { } stake
        && RavenRulesHooks.Tier(stake.Rarity) == RelicRarity.Common
        && Rewards is [RelicModel { Rarity: RelicRarity.Common }]
            ? RavenOutcome.Swap : Outcome;
}

public static partial class RavenServiceHooks
{
    // This is the only submission entry point: a displayable plan means the stake has committed.
    public static async Task<RavenRoundPlan?> SubmitStakeAsync(Player player, RelicModel stake, long expectedRevision = -1)
    {
        if (player is null || !RavenStakeEligibilityHooks.CanStake(player, stake)) return null;
        var state = RavenStorageHooks.Get(player);
        if (!CanStart(player, state, expectedRevision) || state.Pending is not null
            || !GetStakes(player).Any(r => ReferenceEquals(r, stake))) return null;
        try
        {
            var pending = CreatePendingRound(player, state, stake);
            BindOriginal(player, pending, stake);
            bool committed = false;
            await RunOperationAsync(player, state, async () =>
                committed = await RavenTransactionHooks.CommitAsync(state, pending,
                    () => RelicCmd.Remove(stake), () => PersistCurrent(player)));
            if (!_lifetime.Owns(player) || !committed || state.Faulted || !ReferenceEquals(state.Pending, pending)) return null;
            Logger.Log($"[Raven] Submitted revision={state.Revision} round={state.Round} stake={stake.Id} result={pending.Outcome} consumed={pending.StakeConsumed}.");
            return BuildPlan(player, state);
        }
        catch (Exception error)
        {
            RavenTransactionHooks.Fault(state, error);
            SaveAndNotify(player);
            return null;
        }
    }

    private static RavenPendingRound CreatePendingRound(Player player, RavenState state, RelicModel stake)
    {
        var outcome = RavenRulesHooks.Roll(state.Lights,
            RavenStorageHooks.Rng(player, "outcome"), stake.Rarity, out int roll);
        var pending = CreateOffer(player, stake, outcome, RavenStorageHooks.Rng(player, "reward"));
        pending.NextLights = outcome == RavenOutcome.Swallow ? state.Lights
            : RavenRulesHooks.NextLights(state.Lights, RavenStorageHooks.Rng(player, "lights").NextInt(0, 100));
        Logger.Log($"[Raven] Draw stake={stake.Id} lights={state.Lights} roll={roll} rolled={outcome} target={pending.Target} " +
            $"result={pending.Outcome} candidates={pending.Candidates.Count} excluded={pending.ExcludedCandidates} shortage={pending.Shortage}.");
        return pending;
    }

    internal static RavenPendingRound CreateOffer(Player player, RelicModel stake, RavenOutcome outcome, MegaCrit.Sts2.Core.Random.Rng rng)
    {
        var target = RavenRulesHooks.Target(stake.Rarity, outcome);
        List<RelicModel> candidates = [];
        string? ancientId = null;
        int excluded = 0;
        if (outcome == RavenOutcome.Bonus && target == RelicRarity.Ancient)
            (ancientId, candidates) = RavenPoolHooks.CreateAncientBonusCandidates(player, rng, out excluded, stake.Id.ToString());
        else if (outcome is not (RavenOutcome.Swallow or RavenOutcome.Keep))
            candidates = RavenPoolHooks.CreateCandidates(player, target, outcome == RavenOutcome.Bonus ? 3 : 1, rng, out excluded, stake.Id.ToString());
        var pending = new RavenPendingRound
        {
            SettlementVersion = RavenTransactionHooks.CurrentVersion,
            OriginalState = outcome == RavenOutcome.Bonus ? RavenOriginalState.Pending : RavenOriginalState.None,
            AncientId = ancientId,
            StakeSnapshot = stake.ToSerializable(), Outcome = outcome, StakeId = stake.Id.ToString(),
            StakeIndex = player.Relics.ToList().IndexOf(stake), StakeTitle = stake.Title.GetFormattedText(),
            Target = target, ExcludedCandidates = excluded, Candidates = candidates.Select(r => r.ToSerializable()).ToList()
        };
        if (outcome == RavenOutcome.Bonus ? candidates.Count != 3 : IsReplacement(outcome) && candidates.Count == 0)
            RavenTransactionHooks.MarkShortage(pending, outcome, returned: true);
        return pending;
    }

    public static RavenRoundPlan? GetPendingPlan(Player player)
    {
        var state = RavenStorageHooks.Get(player);
        return state.Faulted || state.InFlight || state.Pending is not { } pending
            || !RavenTransactionHooks.IsReady(pending) || HasUnavailableOffer(player, pending) ? null : BuildPlan(player, state);
    }

    internal static RavenRoundPlan BuildPlan(Player player, RavenState state)
    {
        var pending = state.Pending!;
        // A removed instance is never resurrected. The clone exists only for animation and tips.
        var stake = pending.StakeSnapshot is { } snapshot ? RelicModel.FromSerializable(snapshot) : ResolveStake(player, pending);
        if (stake is not null) stake.Owner = player;
        IReadOnlyList<RelicModel> rewards = pending.Outcome switch
        {
            RavenOutcome.Swallow => [],
            RavenOutcome.Bonus when pending.BonusResolved => [],
            RavenOutcome.Keep => stake is null ? [] : [stake],
            _ => pending.Candidates.Select(saved =>
            {
                var relic = RelicModel.FromSerializable(saved);
                relic.Owner = player;
                return relic;
            }).ToList()
        };
        int after = pending.NextLights < 0 ? state.Lights : pending.NextLights;
        return new RavenRoundPlan(pending.Outcome, stake, rewards, pending.Target, state.Lights, after, GetPendingOriginal(player))
            { OriginalState = pending.OriginalState, Notice = pending.Notice };
    }

    public static async Task ExitAsync(Player player, long expectedRevision = -1)
    {
        if (!ReferenceEquals(CurrentPlayer, player)) return;
        var state = RavenStorageHooks.Get(player);
        if (expectedRevision >= 0 && expectedRevision != state.Revision) return;
        if (Busy) { _lifetime.ExitRequested = true; return; }
        await RunOperationAsync(player, state, () => AbandonCoreAsync(player, state, state.RoomKey != RoomKey(player)));
    }
}

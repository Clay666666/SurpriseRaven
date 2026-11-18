using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace SurpriseRaven.Hooks.Raven;

// Bind once per pending object. Losing this exact instance must never select another duplicate.
internal sealed class RavenOriginalIdentityHooks
{
    private readonly RelicModel? _original;
    internal bool Ambiguous { get; }
    internal RavenOriginalIdentityHooks(RelicModel original) => _original = original;
    private RavenOriginalIdentityHooks(RelicModel? original, bool ambiguous) { _original = original; Ambiguous = ambiguous; }
    internal RelicModel? Find(IEnumerable<RelicModel> inventory) => _original is { HasBeenRemovedFromState: false }
        && inventory.Any(r => ReferenceEquals(r, _original)) ? _original : null;

    internal static RavenOriginalIdentityHooks Restore(IEnumerable<RelicModel> inventory, RavenPendingRound pending)
    {
        var relics = inventory.ToArray();
        var indexed = pending.StakeIndex >= 0 ? relics.ElementAtOrDefault(pending.StakeIndex) : null;
        if (indexed is { HasBeenRemovedFromState: false } && indexed.Id.ToString() == pending.StakeId)
            return new RavenOriginalIdentityHooks(indexed);
        var matches = relics.Where(r => !r.HasBeenRemovedFromState && r.Id.ToString() == pending.StakeId).ToArray();
        return new RavenOriginalIdentityHooks(matches.Length == 1 ? matches[0] : null, matches.Length > 1);
    }
}

public static partial class RavenServiceHooks
{
    private sealed record OriginalBinding(RavenPendingRound Pending, RavenOriginalIdentityHooks Identity);
    private static readonly ConditionalWeakTable<Player, OriginalBinding> Originals = new();

    private static RavenOriginalIdentityHooks OriginalIdentity(Player player, RavenPendingRound pending)
    {
        if (Originals.TryGetValue(player, out var binding) && ReferenceEquals(binding.Pending, pending)) return binding.Identity;
        var identity = RavenOriginalIdentityHooks.Restore(player.Relics, pending);
        Originals.Remove(player);
        Originals.Add(player, new OriginalBinding(pending, identity));
        return identity;
    }

    private static void BindOriginal(Player player, RavenPendingRound pending, RelicModel stake)
    {
        Originals.Remove(player);
        if (pending.Outcome is RavenOutcome.Keep or RavenOutcome.Bonus)
            Originals.Add(player, new OriginalBinding(pending, new RavenOriginalIdentityHooks(stake)));
    }

    public static RelicModel? GetPendingOriginal(Player player)
    {
        if (!ReferenceEquals(CurrentPlayer, player)) return null;
        var state = RavenStorageHooks.Get(player);
        return !state.Faulted && state.Pending is { Outcome: RavenOutcome.Bonus, OriginalState: RavenOriginalState.Pending } pending
            && pending.SettlementVersion == RavenTransactionHooks.CurrentVersion
            ? OriginalIdentity(player, pending).Find(player.Relics) : null;
    }

    // The Keep reward already has its own ground slot. Only the inventory view uses this query.
    internal static RelicModel? GetRetainedStakeForDisplay(Player player) =>
        ReferenceEquals(CurrentPlayer, player) ? GetRetainedStakeForDisplay(player, RavenStorageHooks.Get(player)) : null;

    internal static RelicModel? GetRetainedStakeForDisplay(Player player, RavenState state)
    {
        if (state.Faulted || state.Pending is not { StakeConsumed: false } pending || !RavenTransactionHooks.IsReady(pending))
            return null;
        return pending.Outcome == RavenOutcome.Keep
            || pending is { Outcome: RavenOutcome.Bonus, OriginalState: RavenOriginalState.Pending }
            ? OriginalIdentity(player, pending).Find(player.Relics) : null;
    }

    private static bool ReconcileOriginal(Player player, RavenState state)
    {
        if (state.InFlight || state.Faulted || state.Pending is not { Outcome: RavenOutcome.Bonus,
                OriginalState: RavenOriginalState.Pending } pending) return false;
        var identity = OriginalIdentity(player, pending);
        if (identity.Ambiguous)
        {
            RavenTransactionHooks.Fault(state, new InvalidOperationException("Saved original identity is ambiguous; refusing to select another relic."));
            return true;
        }
        var original = identity.Find(player.Relics);
        if (original is null)
        {
            RavenTransactionHooks.CollectOriginal(state, pending, false, () => { });
            Logger.Log($"[Raven] Original {pending.StakeId} was removed by another effect; no reconstruction.");
            return true;
        }
        int index = player.Relics.ToList().FindIndex(r => ReferenceEquals(r, original));
        if (pending.StakeIndex == index) return false;
        pending.StakeIndex = index;
        return true;
    }
}

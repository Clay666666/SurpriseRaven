using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenServiceHooks
{
    private static async Task AbandonCoreAsync(Player player, RavenState state, bool expire)
    {
        await RavenTransactionHooks.AbandonAsync(state, () =>
        {
            var pending = state.Pending!;
            var identity = OriginalIdentity(player, pending);
            if (identity.Ambiguous) throw new InvalidOperationException("Ambiguous saved original; refusing to remove another relic.");
            var original = identity.Find(player.Relics);
            Logger.Log($"[Raven] Forfeit unclaimed original {pending.StakeId}, present={original is not null}, expiry={expire}.");
            return original is null ? Task.CompletedTask : RelicCmd.Remove(original);
        }, () => RavenStorageHooks.Touch(player), expire);
    }

    private static Task ExpireOpportunitiesAsync(Player player)
    {
        var state = RavenStorageHooks.Get(player);
        return RunOperationAsync(player, state, () => AbandonCoreAsync(player, state, expire: true));
    }
}

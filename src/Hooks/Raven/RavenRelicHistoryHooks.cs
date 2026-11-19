using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

internal static class RavenRelicHistoryHooks
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> PickupCallbacks = new();

    internal static bool HasPickupBenefit(RelicModel relic) => relic.HasUponPickupEffect || relic.SpawnsPets
        || RavenAncientStakeRestrictionsHooks.Contains(relic)
        || PickupCallbacks.GetOrAdd(relic.GetType(), static type =>
            type.GetMethod(nameof(RelicModel.AfterObtained), Type.EmptyTypes)?.DeclaringType != typeof(RelicModel));

    internal static bool CanAcquire(Player player, RelicModel relic, bool includePurchases = true) =>
        !player.Relics.Any(held => held.Id == relic.Id)
        && (!HasPickupBenefit(relic) || !GetObtainedRelicIds(player, includePurchases).Contains(relic.Id));

    internal static HashSet<ModelId> GetObtainedRelicIds(Player player, bool includePurchases = true)
    {
        var run = player.RunState;
        var ids = player.Relics.Select(relic => relic.Id).ToHashSet();
        var history = run.MapPointHistory.SelectMany(act => act).SelectMany(point => point.PlayerStats);
        IncludeObtained(ids, player.NetId, history, includePurchases);
        return ids;
    }

    internal static void IncludeObtained(HashSet<ModelId> ids, ulong playerId,
        IEnumerable<PlayerMapPointHistoryEntry> history, bool includePurchases = true)
    {
        foreach (var entry in history)
        {
            if (entry.PlayerId != playerId) continue;
            ids.UnionWith(entry.RelicChoices.Where(choice => choice.wasPicked).Select(choice => choice.choice));
            if (includePurchases) ids.UnionWith(entry.BoughtRelics);
            ids.UnionWith(entry.RelicsRemoved);
        }
    }
}

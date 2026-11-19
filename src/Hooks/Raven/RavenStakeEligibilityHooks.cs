using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SurpriseRaven.Hooks.Raven;

// Stake restrictions must not change acquisition opportunities or the reward pool.
internal static class RavenStakeEligibilityHooks
{
    private static readonly Lazy<HashSet<ModelId>> UpgradedStarterIds = new(() =>
    {
        var refinement = ModelDb.Relic<TouchOfOrobas>();
        var fallbackId = ModelDb.Relic<Circlet>().Id;
        return ModelDb.AllRelics.Where(r => r.Rarity == RelicRarity.Starter)
            .Select(r => refinement.GetUpgradedStarterRelic(r).Id)
            .Where(id => id != fallbackId).ToHashSet();
    });

    internal static bool IsStarterOrUpgrade(RelicModel relic) =>
        relic.Rarity == RelicRarity.Starter || UpgradedStarterIds.Value.Contains(relic.Id);

    internal static bool CanStake(RelicModel relic) => !relic.HasBeenRemovedFromState
        && !RavenRulesHooks.IsExcluded(relic) && !IsStarterOrUpgrade(relic)
        && !RavenNeowRelicsHooks.Contains(relic.Id)
        && !IsBlockedOffering(relic);

    private static bool IsBlockedOffering(RelicModel relic) =>
        relic.IsWax || RavenAncientStakeRestrictionsHooks.Contains(relic);
}

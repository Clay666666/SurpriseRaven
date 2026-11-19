using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

// A separate draw pool: do not pop the player's normal reward grab bag.
internal static class RavenPoolHooks
{
    internal static List<RelicModel> CreateCandidates(Player player, RelicRarity rarity,
        int count, Rng rng, string? excludeId = null) => CreateCandidates(player, rarity, count, rng, out _, excludeId);

    internal static List<RelicModel> CreateCandidates(Player player, RelicRarity rarity,
        int count, Rng rng, out int excluded, string? excludeId = null, RunManager? manager = null)
    {
        excluded = 0;
        if (count <= 0 || rarity is not (RelicRarity.Common or RelicRarity.Uncommon
                or RelicRarity.Rare or RelicRarity.Ancient)) return [];

        var source = rarity == RelicRarity.Ancient
            ? UnlockedAncients(player).SelectMany(AncientRelics)
            : ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(player.UnlockState)
                .Concat(player.Character.RelicPool.GetUnlockedRelics(player.UnlockState));
        var unavailable = RavenRelicAvailabilityHooks.GetUnavailableRelicIds(player, manager);
        var pool = source.Where(r => r.Rarity == rarity && r.Id.ToString() != excludeId)
            .DistinctBy(r => r.Id).OrderBy(r => r.Id.ToString(), StringComparer.Ordinal).ToList();
        excluded = pool.Count(relic => unavailable.Contains(relic.Id));
        return Draw(player, pool, count, rng, unavailable);
    }

    internal static (string? AncientId, List<RelicModel> Candidates) CreateAncientBonusCandidates(
        Player player, Rng rng, string? excludeId = null) => CreateAncientBonusCandidates(player, rng, out _, excludeId);

    internal static (string? AncientId, List<RelicModel> Candidates) CreateAncientBonusCandidates(
        Player player, Rng rng, out int excluded, string? excludeId = null)
    {
        var unavailable = RavenRelicAvailabilityHooks.GetUnavailableRelicIds(player);
        var ancients = UnlockedAncients(player).OrderBy(ancient => ancient.Id.ToString(), StringComparer.Ordinal).ToArray();
        excluded = ancients.SelectMany(AncientRelics).Select(relic => relic.Id).Distinct().Count(unavailable.Contains);
        var groups = ancients
            .Select(ancient => (ancient.Id, Pool: AncientRelics(ancient)
                .Where(relic => relic.Id.ToString() != excludeId && CanOffer(player, relic.ToMutable(), unavailable))
                .OrderBy(relic => relic.Id.ToString(), StringComparer.Ordinal).ToList()))
            .Where(group => group.Pool.Count >= 3).ToList();
        while (groups.Count > 0)
        {
            int index = rng.NextInt(groups.Count);
            var selected = groups[index];
            var candidates = Draw(player, selected.Pool, 3, rng, unavailable);
            if (candidates.Count == 3) return (selected.Id.ToString(), candidates);
            groups.RemoveAt(index);
        }
        return (null, []);
    }

    private static List<RelicModel> Draw(Player player, List<RelicModel> pool, int count, Rng rng,
        HashSet<ModelId> unavailable)
    {
        RavenShuffleHooks.Shuffle(pool, rng);
        List<RelicModel> result = [];
        foreach (var canonical in pool)
        {
            var candidate = canonical.ToMutable();
            if (!CanOffer(player, candidate, unavailable)) continue;
            if (!RavenAncientSetupHooks.Initialize(player, candidate, rng)) continue;
            result.Add(candidate);
            if (result.Count == count) break;
        }
        return result;
    }

    private static bool CanOffer(Player player, RelicModel relic, HashSet<ModelId> unavailable)
    {
        if (relic.Rarity is not (RelicRarity.Common or RelicRarity.Uncommon
                or RelicRarity.Rare or RelicRarity.Ancient)) return false;
        if (RavenRulesHooks.IsExcluded(relic) || unavailable.Contains(relic.Id)) return false;
        if (!relic.IsAllowed(player.RunState)) return false;
        return relic.Rarity != RelicRarity.Ancient || RavenAncientEligibilityHooks.IsAllowed(player, relic);
    }

    internal static bool CanClaim(Player player, RelicModel relic) =>
        CanOffer(player, relic, RavenRelicAvailabilityHooks.GetUnavailableRelicIds(player))
        && RavenAncientSetupHooks.HasValidSavedParameters(player, relic);

    internal static IEnumerable<AncientEventModel> UnlockedAncients(Player player)
    {
        var unlocks = player.UnlockState;
        return ModelDb.Acts.Where(a => a.IsUnlocked(unlocks))
            .SelectMany(a => a.GetUnlockedAncients(unlocks)).Concat(unlocks.SharedAncients)
            .Where(ancient => ancient is not Neow)
            .DistinctBy(a => a.Id);
    }

    internal static IEnumerable<RelicModel> AncientRelics(AncientEventModel ancient) =>
        ancient.AllPossibleOptions.Select(option => option.Relic).OfType<RelicModel>()
            .Where(relic => relic.Rarity == RelicRarity.Ancient && !RavenNeowRelicsHooks.Contains(relic.Id))
            .DistinctBy(relic => relic.Id).Select(relic => ModelDb.GetById<RelicModel>(relic.Id));
}

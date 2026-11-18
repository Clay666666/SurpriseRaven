using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

internal static class RavenRelicHistoryHooks
{
    internal static HashSet<ModelId> GetObtainedRelicIds(Player player)
    {
        var run = player.RunState;
        var ids = player.Relics.Select(relic => relic.Id).ToHashSet();
        var history = run.MapPointHistory.SelectMany(act => act).SelectMany(point => point.PlayerStats);
        IncludeObtained(ids, player.NetId, history);
        return ids;
    }

    internal static void IncludeObtained(HashSet<ModelId> ids, ulong playerId,
        IEnumerable<PlayerMapPointHistoryEntry> history)
    {
        foreach (var entry in history)
        {
            if (entry.PlayerId != playerId) continue;
            ids.UnionWith(entry.RelicChoices.Where(choice => choice.wasPicked).Select(choice => choice.choice));
            ids.UnionWith(entry.BoughtRelics);
            ids.UnionWith(entry.RelicsRemoved);
        }
    }
}

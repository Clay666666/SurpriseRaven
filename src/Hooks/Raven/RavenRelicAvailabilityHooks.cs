using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

internal static class RavenRelicAvailabilityHooks
{
    private static readonly PropertyInfo? MerchantInventories = typeof(MerchantRoom).GetProperty("Inventories");
    private static readonly PropertyInfo? MerchantSynchronizer = typeof(RunManager).GetProperty("MerchantSynchronizer");
    private static readonly MethodInfo? GetInventoryFor = MerchantSynchronizer?.PropertyType
        .GetMethod("GetInventoryFor", [typeof(Player)]);

    internal static HashSet<ModelId> GetUnavailableRelicIds(Player player, RunManager? manager = null)
    {
        var ids = RavenRelicHistoryHooks.GetObtainedRelicIds(player);
        RavenRewardReservationHooks.IncludePending(player, ids);

        manager ??= RunManager.Instance;
        if (!ReferenceEquals(manager.DebugOnlyGetState(), player.RunState)) return ids;
        var run = player.RunState;
        if ((run.CurrentRoom as MerchantRoom ?? run.BaseRoom as MerchantRoom) is { } shop)
            IncludeShopStock(GetInventory(shop, player, MerchantSynchronizer?.GetValue(manager)), ids);
        if (run.BaseRoom is TreasureRoom || run.CurrentRoom is TreasureRoom)
        {
            var synchronizer = manager.TreasureRoomRelicSynchronizer
                ?? throw new InvalidOperationException("Missing treasure room relic synchronizer.");
            IncludeTreasureStock(synchronizer.CurrentRelics, ids);
        }
        return ids;
    }

    private static MerchantInventory GetInventory(MerchantRoom room, Player player, object? synchronizer)
    {
        // The standard-room inventory moved from MerchantRoom in 111 to MerchantSynchronizer in 113.
        var inventory = MerchantInventories is not null
            ? (MerchantInventories.GetValue(room) as IEnumerable<MerchantInventory>)?
                .SingleOrDefault(entry => ReferenceEquals(entry.Player, player))
            : GetInventoryFor is not null && synchronizer is not null
                ? GetInventoryFor.Invoke(synchronizer, [player]) as MerchantInventory
                : null;
        if (inventory is null || !ReferenceEquals(inventory.Player, player))
            throw new InvalidOperationException("Cannot read the current player's merchant inventory.");
        return inventory;
    }

    private static void IncludeShopStock(MerchantInventory inventory, HashSet<ModelId> ids)
    {
        foreach (var entry in inventory.RelicEntries)
            if (entry.IsStocked && entry.Model is { } relic)
                ids.Add(relic.Id);
    }

    private static void IncludeTreasureStock(IReadOnlyList<RelicModel>? relics, HashSet<ModelId> ids)
    {
        if (relics is null) return;
        foreach (var relic in relics) ids.Add(relic.Id);
    }
}

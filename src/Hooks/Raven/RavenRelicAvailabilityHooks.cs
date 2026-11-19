using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

internal static class RavenRelicAvailabilityHooks
{
    private static readonly PropertyInfo? MerchantInventories = typeof(MerchantRoom).GetProperty("Inventories");
    private static readonly PropertyInfo? MerchantSynchronizer = typeof(RunManager).GetProperty("MerchantSynchronizer");
    private static readonly FieldInfo? SyncedInventories = MerchantSynchronizer?.PropertyType
        .GetField("_inventories", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static HashSet<ModelId> GetUnavailableRelicIds(Player player, RunManager? manager = null,
        bool includeLamp = false, EventModel? exceptEvent = null, bool includeTreasure = true, RewardsSet? exceptRewards = null)
    {
        var ids = GetUnavailableForNewOffer(player);
        RavenRewardReservationHooks.IncludePending(player, ids, exceptRewards);
        RavenEventOfferHooks.IncludePending(player, ids, exceptEvent);
        if (includeLamp) IncludeLampOffers(player, ids);

        manager ??= RunManager.Instance;
        if (!ReferenceEquals(manager.DebugOnlyGetState(), player.RunState)) return ids;
        var run = player.RunState;
        if ((run.CurrentRoom as MerchantRoom ?? run.BaseRoom as MerchantRoom) is { } shop
            && GetInventory(shop, player, MerchantSynchronizer?.GetValue(manager)) is { } inventory)
            IncludeShopStock(inventory, ids);
        if (includeTreasure && (run.BaseRoom is TreasureRoom || run.CurrentRoom is TreasureRoom))
            IncludeTreasureStock(manager.TreasureRoomRelicSynchronizer?.CurrentRelics, ids);
        return ids;
    }

    internal static HashSet<ModelId> GetUnavailableForNewOffer(Player player)
    {
        var ids = player.Relics.Select(relic => relic.Id).ToHashSet();
        foreach (var id in RavenRelicHistoryHooks.GetObtainedRelicIds(player))
            if (ModelDb.GetByIdOrNull<RelicModel>(id) is not { } relic || RavenRelicHistoryHooks.HasPickupBenefit(relic))
                ids.Add(id);
        return ids;
    }

    internal static void IncludeLampOffers(Player player, HashSet<ModelId> ids)
    {
        if (PendingLampOffer(player) is not { } offer) return;
        foreach (var candidate in offer.Candidates)
            if (candidate.Id is { } id) ids.Add(id);
    }

    internal static bool IsLampOffer(Player player, ModelId id) =>
        PendingLampOffer(player)?.Candidates.Any(candidate => candidate.Id == id) == true;

    private static RavenPendingRound? PendingLampOffer(Player player)
    {
        if (player.RunState is not RunState) return null;
        var lamp = RavenStorageHooks.Get(player);
        return lamp.RoomKey == RavenServiceHooks.RoomKey(player) ? lamp.Pending : null;
    }

    private static MerchantInventory? GetInventory(MerchantRoom room, Player player, object? synchronizer)
    {
        // The standard-room inventory moved from MerchantRoom in 111 to MerchantSynchronizer in 113.
        var inventories = MerchantInventories is not null ? MerchantInventories.GetValue(room)
            : synchronizer is not null ? SyncedInventories?.GetValue(synchronizer) : null;
        return (inventories as IEnumerable<MerchantInventory>)?.SingleOrDefault(entry => ReferenceEquals(entry.Player, player));
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

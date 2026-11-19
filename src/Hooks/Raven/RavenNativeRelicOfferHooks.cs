using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

[HarmonyPatch]
internal static class RavenNativeRelicOfferHooks
{
    private static readonly MethodInfo ClearStock = AccessTools.PropertySetter(typeof(MerchantRelicEntry), nameof(MerchantRelicEntry.Model));

    [HarmonyPatch]
    private static class InitialShopOffers
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(MerchantInventory), nameof(MerchantInventory.CreateForNormalMerchant));
            if (AccessTools.DeclaredMethod(typeof(MerchantInventory), "CreateForBlitzMerchant") is { } blitz) yield return blitz;
        }

        [HarmonyPostfix]
        private static void FilterNewShopOffers(MerchantInventory __result)
        {
            if (!RavenServiceHooks.IsSingleplayer) return;
            var ids = RavenRelicAvailabilityHooks.GetUnavailableRelicIds(__result.Player, includeLamp: true);
            foreach (var slot in __result.RelicEntries)
            {
                if (slot.Model is not { } relic || ids.Add(relic.Id)) continue;
                ClearStock.Invoke(slot, [null]);
                Logger.Log($"[Raven] Initial shop stock excluded before display: {relic.Id}.");
            }
        }
    }

    [HarmonyPatch]
    private static class GrabBagOffers
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] arguments = [typeof(RelicRarity), typeof(Func<RelicModel, bool>), typeof(IRunState)];
            yield return AccessTools.DeclaredMethod(typeof(RelicGrabBag), nameof(RelicGrabBag.PullFromFront), arguments);
            yield return AccessTools.DeclaredMethod(typeof(RelicGrabBag), nameof(RelicGrabBag.PullFromBack), arguments);
        }

        [HarmonyPrefix]
        private static void FilterNativeRelicDraw(IRunState runState, ref Func<RelicModel, bool> filter)
        {
            if (!RavenServiceHooks.IsSingleplayer || !ReferenceEquals(runState, RunManager.Instance.DebugOnlyGetState())
                || runState.Players.FirstOrDefault() is not { } player) return;
            var unavailable = RavenRelicAvailabilityHooks.GetUnavailableRelicIds(player, includeLamp: true);
            var original = filter;
            filter = relic => !unavailable.Contains(relic.Id) && original(relic);
        }
    }

    [HarmonyPatch(typeof(MerchantRelicEntry), "SetModel"), HarmonyPrefix]
    private static bool FilterShopModel(MerchantRelicEntry __instance, Player ____player, RelicModel model)
    {
        if (!RavenServiceHooks.IsSingleplayer || ReferenceEquals(model, __instance.Model)
            || !RavenRelicAvailabilityHooks.GetUnavailableRelicIds(____player, includeLamp: true).Contains(model.Id))
            return true;
        Logger.Log($"[Raven] Shop stock excluded before display: {model.Id}.");
        return false;
    }

    [HarmonyPatch]
    private static class ShopPurchase
    {
        private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(typeof(MerchantRelicEntry), "OnTryPurchaseInternal")
            ?? AccessTools.DeclaredMethod(typeof(MerchantRelicEntry), "OnTryPurchase");

        [HarmonyPrefix]
        private static bool ValidateShopPurchase(MerchantRelicEntry __instance, Player ____player, ref Task<(bool, int)> __result)
        {
            if (!RavenServiceHooks.IsSingleplayer || (__instance.Model is { } relic && CanClaimNative(____player, relic)))
                return true;
            __result = Task.FromResult((false, 0));
            return false;
        }
    }

    [HarmonyPatch(typeof(RelicReward), "OnSelect"), HarmonyPrefix]
    private static bool ValidateRewardPickup(RelicReward __instance, ref Task<bool> __result)
    {
        if (!RavenServiceHooks.IsSingleplayer || (__instance.Relic is { } relic && CanClaimNative(__instance.Player, relic)))
            return true;
        __result = Task.FromResult(false);
        return false;
    }

    [HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Replace)), HarmonyPrefix]
    private static bool ValidateNativeReplacement(RelicModel original, RelicModel replace, ref Task<RelicModel> __result)
    {
        if (!RavenServiceHooks.IsSingleplayer || CanClaimNative(original.Owner, replace)) return true;
        __result = Task.FromResult(original);
        return false;
    }

    [HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking)), HarmonyPostfix]
    private static void FilterNewChestRelics(List<RelicModel>? ____currentRelics)
    {
        if (!RavenServiceHooks.IsSingleplayer || ____currentRelics is null
            || RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault() is not { } player) return;
        var ids = RavenRelicAvailabilityHooks.GetUnavailableRelicIds(player, includeLamp: true, includeTreasure: false);
        int removed = ____currentRelics.RemoveAll(relic => !ids.Add(relic.Id));
        if (removed != 0) Logger.Log($"[Raven] Chest excluded {removed} overlapping relics before display.");
    }

    [HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.PickRelicLocally)), HarmonyPrefix]
    private static bool ValidateChestPickup(TreasureRoomRelicSynchronizer __instance, int? index) =>
        !RavenServiceHooks.IsSingleplayer || index is not { } selected || __instance.CurrentRelics is not { } pool
        || selected < 0 || selected >= pool.Count || RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault() is not { } player
        || CanClaimNative(player, pool[selected]);

    internal static bool CanClaimNative(Player player, RelicModel relic, bool includePurchases = true) =>
        !RavenRelicAvailabilityHooks.IsLampOffer(player, relic.Id) && RavenRelicHistoryHooks.CanAcquire(player, relic, includePurchases);
}

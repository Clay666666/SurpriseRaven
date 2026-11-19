using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;

namespace SurpriseRaven.Hooks.Raven;

[HarmonyPatch]
internal static class RavenEventOfferHooks
{
    private sealed record ActiveEvent(EventModel Event, AbstractRoom? Room);
    private static readonly ConditionalWeakTable<Player, ActiveEvent> Active = new();

    internal static void IncludePending(Player player, HashSet<ModelId> ids, EventModel? except)
    {
        if (!Active.TryGetValue(player, out var current) || ReferenceEquals(current.Event, except)
            || !ReferenceEquals(current.Room, player.RunState.BaseRoom) || current.Event.IsFinished) return;
        foreach (var option in current.Event.CurrentOptions)
            if (!option.IsLocked && !option.WasChosen && option.Relic is { } relic) ids.Add(relic.Id);
    }

    [HarmonyPatch(typeof(EventModel), "SetEventState"), HarmonyPrefix]
    private static void FilterNativeEventOptions(EventModel __instance, ref IEnumerable<EventOption> eventOptions)
    {
        if (!RavenServiceHooks.IsSingleplayer || __instance.Owner is not { } player) return;
        var ids = RavenRelicAvailabilityHooks.GetUnavailableRelicIds(player, includeLamp: true, exceptEvent: __instance);
        var original = eventOptions.ToArray();
        var available = original.Where(option => option.Relic is not { } relic || ids.Add(relic.Id)).ToArray();
        if (original.Length != available.Length)
            Logger.Log($"[Raven] Event {__instance.Id}: excluded {original.Length - available.Length} overlapping relic options.");
        eventOptions = available.Length == 0 && original.Length > 0
            ? [new EventOption(__instance, NEventRoom.Proceed, "PROCEED", false, true)]
            : available;
        Active.Remove(player);
        Active.Add(player, new ActiveEvent(__instance, player.RunState.BaseRoom));
    }

    [HarmonyPatch(typeof(EventSynchronizer), "ChooseOptionForEvent"), HarmonyPrefix]
    private static bool ValidateNativeEventPickup(EventSynchronizer __instance, Player player, int optionIndex)
    {
        if (!RavenServiceHooks.IsSingleplayer) return true;
        var options = __instance.GetEventForPlayer(player).CurrentOptions;
        return optionIndex < 0 || optionIndex >= options.Count || options[optionIndex].Relic is not { } relic
            || RavenNativeRelicOfferHooks.CanClaimNative(player, relic);
    }
}

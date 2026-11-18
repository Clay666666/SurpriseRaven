using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

internal static class RavenNativeAncientSelectionHooks
{
    private static readonly FieldInfo RoomsField = AccessTools.Field(typeof(ActModel), "_rooms");

    internal static bool Applies(Player? player) => player is not null && RavenServiceHooks.IsSingleplayer
        && player.RunState.CurrentActIndex is 1 or 2;

    internal static bool IsViable(AncientEventModel canonical, Player player, ISet<ModelId> obtained)
    {
        if (!RavenNativeAncientPoolsHooks.Supported(canonical)) return false;
        var probe = (AncientEventModel)canonical.ToMutable();
        typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(probe, player);
        return RavenNativeAncientPoolsHooks.Build(probe, player, obtained).Viable;
    }

    // Bounded enumeration only: no repeated BeginEvent, healing, or speculative reward RNG.
    internal static AncientEventModel? Choose(AncientEventModel original, IEnumerable<AncientEventModel> candidates,
        Func<AncientEventModel, bool> viable, Func<int, int> next)
    {
        if (viable(original)) return original;
        var alternatives = candidates.Where(a => a.Id != original.Id).DistinctBy(a => a.Id)
            .OrderBy(a => a.Id.ToString(), StringComparer.Ordinal).Where(viable).ToList();
        return alternatives.Count == 0 ? null : alternatives[next(alternatives.Count)];
    }

    internal static void SetAncient(ActModel act, AncientEventModel canonical) =>
        ((RoomSet)RoomsField.GetValue(act)!).Ancient = canonical;

    internal static bool IsExhausted(Player player, AncientEventModel ancient)
    {
        var data = RavenNativeAncientStorageHooks.Get(player);
        return data.Visits.TryGetValue(RavenNativeAncientStorageHooks.ActKey(player, player.RunState.Act), out var visit)
            && visit.AncientId == ancient.Id.ToString() && visit.Exhausted;
    }

    internal static AncientEventModel Resolve(Player player, ActModel act, AncientEventModel original, bool lockUnchanged = false)
    {
        if (!Applies(player) || !RavenNativeAncientPoolsHooks.Supported(original)) return original;
        var data = RavenNativeAncientStorageHooks.Get(player);
        string key = RavenNativeAncientStorageHooks.ActKey(player, act);
        if (data.Visits.TryGetValue(key, out var previous))
        {
            var saved = ModelDb.AllAncients.FirstOrDefault(a => a.Id.ToString() == previous.AncientId);
            if (saved is not null) { SetAncient(act, saved); return saved; }
            return original;
        }
        try
        {
            var obtained = RavenRelicHistoryHooks.GetObtainedRelicIds(player);
            bool affected = RavenPoolHooks.AncientRelics(original).Any(r => obtained.Contains(r.Id));
            if (!affected && !lockUnchanged) return original;
            var chosen = affected ? Choose(original, RavenNativeAncientStorageHooks.Candidates(player, act, data),
                a => IsViable(a, player, obtained), count => RavenStorageHooks.Rng(player, "nativeAncient").NextInt(count)) : original;
            data.Visits[key] = new RavenNativeAncientVisit { AncientId = (chosen ?? original).Id.ToString(), Exhausted = chosen is null };
            if (chosen is not null) SetAncient(act, chosen);
            Logger.Log($"[Raven] Native ancient act={key}, original={original.Id}, selected={chosen?.Id}, exhausted={chosen is null}.");
            RavenNativeAncientStorageHooks.Touch(player);
            return chosen ?? original;
        }
        catch (Exception error)
        {
            data.Visits[key] = new RavenNativeAncientVisit { AncientId = original.Id.ToString(), Exhausted = true };
            RavenNativeAncientStorageHooks.Touch(player);
            Logger.Log($"[Raven] Native ancient selection failed; offer only Continue: {error}");
            return original;
        }
    }

    [HarmonyPatch(typeof(ActModel), nameof(ActModel.PullAncient))]
    private static class PullAncientPatch
    {
        [HarmonyPostfix]
        private static void RavenChooseAvailableAncientPostfix(ActModel __instance, ref EventModel __result)
        {
            var run = RunManager.Instance.DebugOnlyGetState();
            var player = run?.Players.FirstOrDefault();
            if (!Applies(player) || !ReferenceEquals(player!.RunState.Act, __instance)
                || __result is not AncientEventModel original || !RavenNativeAncientPoolsHooks.Supported(original)) return;
            __result = Resolve(player, __instance, original, lockUnchanged: true);
        }
    }
}

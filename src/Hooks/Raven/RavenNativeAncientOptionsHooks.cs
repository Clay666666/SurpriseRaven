using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenNativeAncientOptionsHooks
{
    private sealed class LockedOptions(List<EventOption> options) { internal List<EventOption> Options { get; } = options; }
    private static readonly ConditionalWeakTable<AncientEventModel, LockedOptions> Locked = new();
    internal static readonly Type[] NativeTypes = [typeof(Pael), typeof(Tezcatara), typeof(Vakuu), typeof(Orobas),
        typeof(Nonupeipe), typeof(Tanx), typeof(Darv)];

    internal static List<EventOption> Continue(AncientEventModel ancient) =>
        [new EventOption(ancient, NEventRoom.Proceed, "PROCEED", false, true)];

    // Cache belongs to the mutable event instance; never initialize the same native offer twice.
    internal static List<EventOption> Generate(AncientEventModel ancient, ISet<ModelId> obtained)
    {
        if (Locked.TryGetValue(ancient, out var locked)) return locked.Options;
        var player = ancient.Owner ?? throw new InvalidOperationException("Native ancient has no owner.");
        var plan = RavenNativeAncientPoolsHooks.Build(ancient, player, obtained);
        var result = plan.Viable ? plan.Draw(ancient, player, ancient.Rng) : Continue(ancient);
        Locked.Add(ancient, new LockedOptions(result));
        return result;
    }

    [HarmonyPatch]
    private static class InitialOptionsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => NativeTypes.Select(t =>
            (MethodBase)(AccessTools.DeclaredMethod(t, "GenerateInitialOptions") ?? throw new MissingMethodException(t.FullName, "GenerateInitialOptions")));

        [HarmonyPrefix]
        private static bool RavenFilterNativeAncientOptionsPrefix(AncientEventModel __instance, ref IReadOnlyList<EventOption> __result)
        {
            if (__instance.Owner is not { } player || (!FixtureApplies(player) && !RavenNativeAncientSelectionHooks.Applies(player))) return true;
            try
            {
                var obtained = FixtureApplies(player) ? FixtureObtained! : RavenRelicAvailabilityHooks.GetUnavailableRelicIds(player, includeLamp: true, exceptEvent: __instance);
                // With no affected history, retain the native generator and RNG consumption exactly.
                if (!__instance.AllPossibleOptions.Any(o => o.Relic is { } r && obtained.Contains(r.Id))) return true;
                __result = Generate(__instance, obtained);
                return false;
            }
            catch (Exception error)
            {
                Logger.Log($"[Raven] Native ancient option filtering failed; offer only Continue: {error}");
                __result = Continue(__instance);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(AncientEventModel), "GenerateInitialOptionsWrapper")]
    private static class ExhaustedOptionsPatch
    {
        [HarmonyPrefix]
        private static bool RavenExhaustedAncientContinuePrefix(AncientEventModel __instance, ref IReadOnlyList<EventOption> __result)
        {
            if (__instance.Owner is not { } player) return true;
            bool exhausted = FixtureApplies(player) ? FixtureExhausted : RavenNativeAncientSelectionHooks.Applies(player)
                && RavenNativeAncientSelectionHooks.IsExhausted(player, __instance);
            if (!exhausted) return true;
            __result = Continue(__instance);
            return false;
        }
    }
}

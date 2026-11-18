using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

// The map nodes cache their Ancient icon at construction. Resolve before the native asset load and map build.
[HarmonyPatch(typeof(PreloadManager), nameof(PreloadManager.LoadActAssets), new[] { typeof(ActModel) })]
internal static class RavenNativeAncientPreloadHooks
{
    [HarmonyPrefix]
    private static void RavenResolveAncientBeforePreloadPrefix(ActModel act)
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        var player = run?.Players.FirstOrDefault();
        if (run is null || player is null || !ReferenceEquals(run.Act, act)
            || !RavenNativeAncientSelectionHooks.Applies(player)) return;
        var history = run.MapPointHistory.ElementAtOrDefault(run.CurrentActIndex);
        if (history?.Any(point => point.MapPointType == MapPointType.Ancient) == true) return;
        if (act.Ancient is { } ancient && RavenNativeAncientPoolsHooks.Supported(ancient))
            RavenNativeAncientSelectionHooks.Resolve(player, act, ancient);
    }
}

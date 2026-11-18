using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

[HarmonyPatch]
internal static class RavenMovementHooks
{
    [ThreadStatic]
    private static bool _checkingNonTerminalReward;

    // Both supported APIs reuse the map-proceed hook for non-terminal reward exits.
    // Scope only that synchronous check: closing pickup rewards must finish Obtain,
    // while terminal rewards and actual room movement retain the settlement lock.
    [HarmonyPatch(typeof(NRewardsScreen), "TryEnableProceedButton")]
    [HarmonyPrefix]
    private static void RavenRewardProceedPrefix(bool ____isTerminal, out bool? __state)
    {
        __state = _checkingNonTerminalReward;
        _checkingNonTerminalReward = !____isTerminal;
        if (_checkingNonTerminalReward && RavenServiceHooks.Busy)
            Logger.Log("[Raven] Checking pickup reward exit without the lamp movement lock.");
    }

    [HarmonyPatch(typeof(NRewardsScreen), "TryEnableProceedButton")]
    [HarmonyFinalizer]
    private static void RavenRewardProceedFinalizer(bool? __state)
    {
        // Restore the caller's mode for nested checks, including exceptional exits.
        // Null means another Harmony prefix skipped ours; there is nothing to restore.
        if (__state is bool previous) _checkingNonTerminalReward = previous;
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.ShouldProceedToNextMapPoint))]
    [HarmonyPostfix]
    private static void RavenProceedPostfix(ref bool __result)
    {
        if (RavenServiceHooks.Busy && !_checkingNonTerminalReward) __result = false;
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapCoord))]
    [HarmonyPrefix]
    private static bool RavenMapEntryPrefix(ref Task __result)
    {
        if (!RavenServiceHooks.Busy) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

// Map outranks overlays in ActiveScreenContext and hides them in NOverlayStack.
// Delay its Open call until the current room's lamp offers have been resolved.
[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open), new[] { typeof(bool) })]
internal static class RavenMapGateHooks
{
    private static NMapScreen? _deferred;
    private static bool _openedFromTopBar;
    private static bool _replaying;
    internal static bool HasDeferredMap => _deferred is not null && GodotObject.IsInstanceValid(_deferred);

    internal static void Reset()
    {
        _deferred = null;
        _openedFromTopBar = false;
        _replaying = false;
    }

    internal static bool ShouldDefer(bool singleplayer, bool hasOpportunity, bool hasPanel, bool replaying) =>
        singleplayer && !replaying && (hasOpportunity || hasPanel);

    [HarmonyPrefix]
    private static bool RavenMapOpenPrefix(NMapScreen __instance, bool isOpenedFromTopBar, ref NMapScreen __result)
    {
        if (RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault() is not { } player
            || !RavenServiceHooks.BindLivePlayer(player)) return true;
        if (!ShouldDefer(RavenServiceHooks.IsSingleplayer, RavenServiceHooks.State.HasOpportunity,
                RavenOverlayHooks.HasOpenPanel, _replaying)) return true;
        Defer(__instance, isOpenedFromTopBar);
        __result = __instance;
        Logger.Log("[Raven] Map opening deferred until lamp opportunities finish.");
        return false;
    }

    internal static void Defer(NMapScreen map, bool fromTopBar)
    {
        _deferred = map;
        _openedFromTopBar = fromTopBar;
    }

    internal static void TryResume()
    {
        if (_deferred is null) return;
        if (!GodotObject.IsInstanceValid(_deferred) || !ReferenceEquals(_deferred, NMapScreen.Instance)
            || !RavenServiceHooks.IsSingleplayer)
        {
            _deferred = null;
            return;
        }
        var player = RavenServiceHooks.CurrentPlayer;
        if (player is null || RavenServiceHooks.State.HasOpportunity || RavenOverlayHooks.HasOpenPanel
            || !RavenServiceHooks.CanInteract(player)
            || NModalContainer.Instance?.OpenModal is not null
            || NCapstoneContainer.Instance?.InUse == true
            || NOverlayStack.Instance is not { } stack || !RavenOverlayHooks.CanCoverStack(stack)) return;
        var map = _deferred;
        _deferred = null;
        _replaying = true;
        try
        {
            map.Open(_openedFromTopBar);
            Logger.Log("[Raven] Deferred map opening resumed.");
        }
        finally { _replaying = false; }
    }
}

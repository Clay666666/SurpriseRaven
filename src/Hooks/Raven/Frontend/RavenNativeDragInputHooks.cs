using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace SurpriseRaven.Hooks.Raven;

// Both private fields and both patch signatures verified against the 111 and 113 assemblies.
internal static class RavenNativeDragInputHooks
{
    private static readonly FieldInfo ThresholdField = AccessTools.Field(typeof(NClickableControl), "_ignoreDragThreshold");
    private static readonly FieldInfo PressedField = AccessTools.Field(typeof(NClickableControl), "_isPressed");
    private static WeakReference<RelicModel>? _releasedRelic;
    private static ulong _releasedFrame;
    internal static RavenNativeDragHooks? Owner { get; set; }

    internal static float GetThreshold(NButton button) => (float)ThresholdField.GetValue(button)!;
    internal static void SetThreshold(NButton button, float value) => ThresholdField.SetValue(button, value);
    internal static void ClearNativePress(NButton button) => PressedField.SetValue(button, false);
    internal static bool NativePressed(NButton button) => (bool)PressedField.GetValue(button)!;

    internal static void SuppressInspect(RelicModel relic)
    {
        _releasedRelic = new WeakReference<RelicModel>(relic);
        _releasedFrame = Engine.GetProcessFrames();
    }

    internal static bool SuppressesInspect(RelicModel relic) => _releasedRelic?.TryGetTarget(out var released) == true
        && ReferenceEquals(released, relic)
        && Engine.GetProcessFrames() <= _releasedFrame + 1;

    internal static void ClearSuppression() => _releasedRelic = null;

    [HarmonyPatch(typeof(NButton), nameof(NButton._Input))]
    private static class NativeInputPatch
    {
        [HarmonyPostfix]
        private static void RavenNativeDragInputPostfix(NButton __instance, InputEvent inputEvent) => Owner?.Receive(__instance, inputEvent);
    }

    [HarmonyPatch(typeof(NRelicInventory), "OnRelicClicked")]
    private static class NativeInspectPatch
    {
        [HarmonyPrefix]
        private static bool RavenNativeDragInspectPrefix(RelicModel model) => !SuppressesInspect(model);
    }
}

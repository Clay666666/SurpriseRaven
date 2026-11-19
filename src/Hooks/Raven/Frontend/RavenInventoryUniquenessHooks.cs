using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace SurpriseRaven.Hooks.Raven;

[HarmonyPatch]
internal static class RavenInventoryUniquenessHooks
{
    private sealed record HiddenHolder(Control.FocusModeEnum Focus);
    private static readonly ConditionalWeakTable<Control, HiddenHolder> Hidden = new();

    [HarmonyPatch(typeof(NRelicInventory), "Remove"), HarmonyPrefix]
    private static void HideOutgoingIcon(NRelicInventory __instance, RelicModel relic)
    {
        if (!RavenServiceHooks.IsSingleplayer) return;
        var outgoing = __instance.RelicNodes.FirstOrDefault(holder => ReferenceEquals(holder.Relic.Model, relic));
        outgoing?.Hide();
    }

    [HarmonyPatch]
    private static class InventoryUpdates
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(NRelicInventory), "Add");
            yield return AccessTools.DeclaredMethod(typeof(NRelicInventory), "Remove");
        }

        [HarmonyPostfix]
        private static void KeepOneInventoryIcon(NRelicInventory __instance)
        {
            if (RavenServiceHooks.IsSingleplayer)
                Apply(__instance.RelicNodes.Where(GodotObject.IsInstanceValid).Select(holder => ((Control)holder, holder.Relic.Model.Id)));
        }
    }

    internal static void Apply(IEnumerable<(Control Holder, ModelId Id)> holders)
    {
        HashSet<ModelId> seen = [];
        foreach (var (holder, id) in holders)
        {
            if (!seen.Add(id))
            {
                Hidden.GetValue(holder, control => new HiddenHolder(control.FocusMode));
                holder.FocusMode = Control.FocusModeEnum.None;
                holder.Hide();
            }
            else if (Hidden.TryGetValue(holder, out var hidden))
            {
                Hidden.Remove(holder);
                holder.FocusMode = hidden.Focus;
                holder.Show();
            }
        }
    }
}

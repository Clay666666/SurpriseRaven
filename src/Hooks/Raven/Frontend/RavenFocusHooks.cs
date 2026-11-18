using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace SurpriseRaven.Hooks.Raven;

// Mouse hover and directional navigation must not compete for a persistent focus owner.
internal static class RavenFocusHooks
{
    internal static bool DirectionalNavigation => NControllerManager.Instance?.IsUsingDirectionalNavigation == true;

    internal static void FocusDefault(Control scope, Control preferred, bool preserveExisting = true) =>
        FocusDefault(scope, preferred, DirectionalNavigation, preserveExisting);

    internal static void FocusDefault(Control scope, Control preferred, bool directionalNavigation, bool preserveExisting)
    {
        if (!directionalNavigation || !Usable(scope) || !Usable(preferred)) return;
        var current = scope.GetViewport().GuiGetFocusOwner();
        if (preserveExisting && Usable(current) && (ReferenceEquals(current, scope) || scope.IsAncestorOf(current))) return;
        if (preferred.FocusMode != Control.FocusModeEnum.None) preferred.GrabFocus();
    }

    private static bool Usable(Control? control) => control is not null && GodotObject.IsInstanceValid(control)
        && !control.IsQueuedForDeletion() && control.IsInsideTree() && control.IsVisibleInTree()
        && control is not NClickableControl { IsEnabled: false } && control is not BaseButton { Disabled: true };
}

using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace SurpriseRaven.Hooks.Raven;

// One transform for all lamp visuals. The outer panel and the game's backstop stay full-screen.
internal static class RavenPageLayoutHooks
{
    internal const float DefaultTop = 112f;

    internal static float NativeTop(Control panel)
    {
        var ui = NRun.Instance?.GlobalUi;
        if (InventoryTop(panel, ui?.RelicInventory) is { } top) return top;
        return ui?.TopBar is { } bar && GodotObject.IsInstanceValid(bar) && !bar.IsQueuedForDeletion()
            ? TopBarBottom(panel, [bar.GetNodeOrNull<Control>("BgImage"), bar.Map, bar.Deck, bar.Pause])
            : DefaultTop;
    }

    internal static float? InventoryTop(Control panel, Control? inventory) =>
        VisibleBounds(panel, inventory, allowEmptyHeight: true) is { } bounds ? Math.Max(0, bounds.Position.Y) : null;

    internal static float TopBarBottom(Control panel, IEnumerable<Control?> controls)
    {
        float? bottom = null;
        foreach (var control in controls)
            if (VisibleBounds(panel, control) is { } bounds)
                bottom = Math.Max(bottom ?? 0, Math.Min(bounds.End.Y, panel.Size.Y));
        return bottom ?? DefaultTop;
    }

    private static Rect2? VisibleBounds(Control panel, Control? control, bool allowEmptyHeight = false)
    {
        if (control is null || !GodotObject.IsInstanceValid(control) || control.IsQueuedForDeletion()
            || !control.IsVisibleInTree() || control.Size.X <= 0 || (!allowEmptyHeight && control.Size.Y <= 0))
            return null;
        var toPanel = panel.GetGlobalTransformWithCanvas().AffineInverse() * control.GetGlobalTransformWithCanvas();
        var bounds = toPanel * new Rect2(Vector2.Zero, control.Size);
        if (!bounds.IsFinite() || bounds.End.X <= 0 || bounds.Position.X >= panel.Size.X
            || bounds.Position.Y >= panel.Size.Y || bounds.End.Y < 0
            || (bounds.End.Y == 0 && bounds.Size.Y > 0))
            return null;
        return bounds;
    }

    internal static void Apply(Control content, Vector2 size, float requestedTop)
    {
        if (size.X <= 0 || size.Y <= 0) return;
        float top = Mathf.Clamp(requestedTop, 0, Math.Max(0, size.Y - 1));
        content.Size = size;
        content.Position = new Vector2(0, top);
        content.Scale = new Vector2(1, (size.Y - top) / size.Y);
    }

    internal static Vector2 CenterPreview(Vector2 localMouse, Vector2 previewSize) => localMouse - previewSize / 2;
}

internal sealed partial class RavenLampPanelHooks
{
    private SceneTree? _layoutTree;

    private void StartContentLayout()
    {
        StopContentLayout();
        _layoutTree = GetTree();
        _layoutTree.ProcessFrame += UpdateContentLayout;
        UpdateContentLayout();
    }

    private void StopContentLayout()
    {
        if (_layoutTree is not null && GodotObject.IsInstanceValid(_layoutTree))
            _layoutTree.ProcessFrame -= UpdateContentLayout;
        _layoutTree = null;
    }

    private void UpdateContentLayout()
    {
        if (_closed || !IsInsideTree()) return;
        RavenPageLayoutHooks.Apply(_pageRoot, Size, RavenPageLayoutHooks.NativeTop(this));
    }
}

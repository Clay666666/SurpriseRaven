using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace SurpriseRaven.Hooks.Raven;

// One transform for all lamp visuals. The outer panel and the game's backstop stay full-screen.
internal static class RavenPageLayoutHooks
{
    internal const float Gap = 12f;
    internal const float DefaultTop = 112f;

    internal static float NativeTop(Control panel)
    {
        var ui = NRun.Instance?.GlobalUi;
        List<Control?> controls = [];
        if (ui?.TopBar is { } bar && GodotObject.IsInstanceValid(bar) && !bar.IsQueuedForDeletion())
            controls.AddRange([bar.GetNodeOrNull<Control>("BgImage"), bar.Map, bar.Deck, bar.Pause]);
        if (ui?.RelicInventory is { } inventory && GodotObject.IsInstanceValid(inventory)
            && !inventory.IsQueuedForDeletion() && inventory.IsVisibleInTree())
            controls.AddRange(inventory.RelicNodes);
        return TopBoundary(panel, controls);
    }

    internal static float TopBoundary(Control panel, IEnumerable<Control?> controls)
    {
        var inverse = panel.GetGlobalTransform().AffineInverse();
        var visibleArea = panel.GetGlobalRect();
        float? bottom = null;
        foreach (var control in controls)
        {
            if (control is null || !GodotObject.IsInstanceValid(control) || control.IsQueuedForDeletion() || !control.IsVisibleInTree()
                || control.Size.X <= 0 || control.Size.Y <= 0) continue;
            var rect = control.GetGlobalRect();
            if (!rect.Intersects(visibleArea)) continue;
            rect = rect.Intersection(visibleArea);
            float y = Math.Max((inverse * rect.Position).Y, (inverse * rect.End).Y);
            y = Math.Max(y, (inverse * new Vector2(rect.End.X, rect.Position.Y)).Y);
            y = Math.Max(y, (inverse * new Vector2(rect.Position.X, rect.End.Y)).Y);
            if (float.IsFinite(y)) bottom = Math.Max(bottom ?? y, y);
        }
        return bottom is { } value ? Mathf.Ceil(value) + Gap : DefaultTop;
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

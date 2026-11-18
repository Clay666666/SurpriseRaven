using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace SurpriseRaven.Hooks.Raven;

// Native shortcuts receive input even when a child owns focus, without generated Godot overrides.
internal sealed class RavenPanelInputHooks : IDisposable
{
    private readonly Control _panel;
    private readonly Func<bool> _allowed;
    private readonly Button _pickup;
    private readonly Button? _exit;
    private readonly SceneTree _tree;
    private bool _disposed;

    internal RavenPanelInputHooks(Control panel, Func<bool> allowed, Action pickup, Action exit, bool nativeBack)
    {
        _panel = panel;
        _allowed = allowed;
        _pickup = Shortcut(Key.F, pickup);
        if (!nativeBack) _exit = Shortcut(Key.Escape, exit);
        _tree = panel.GetTree();
        _tree.ProcessFrame += Refresh;
        Refresh();
    }

    private Button Shortcut(Key key, Action action)
    {
        var button = new Button
        {
            Name = $"RavenShortcut{key}", Position = new Vector2(-20, -20), Size = Vector2.One,
            Modulate = Colors.Transparent, MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None, ShortcutFeedback = false, ShortcutInTooltip = false,
            Shortcut = new Shortcut { Events = [new InputEventKey { PhysicalKeycode = key }] }
        };
        button.Pressed += () => { if (CanActivate()) action(); };
        _panel.AddChild(button);
        return button;
    }

    private bool CanActivate() => !_disposed && GodotObject.IsInstanceValid(_panel)
        && _panel.IsInsideTree() && _panel.IsVisibleInTree() && _allowed()
        && _panel.GetViewport().GuiGetFocusOwner() is not LineEdit and not TextEdit;

    internal void Refresh()
    {
        if (_disposed) return;
        bool allowed = CanActivate();
        _pickup.SetProcessShortcutInput(allowed);
        _exit?.SetProcessShortcutInput(allowed);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(_tree)) _tree.ProcessFrame -= Refresh;
        foreach (var button in new[] { _pickup, _exit })
        {
            if (button is null || !GodotObject.IsInstanceValid(button)) continue;
            button.SetProcessShortcutInput(false);
            button.Shortcut = null;
            button.GetParent()?.RemoveChild(button);
            button.QueueFree();
        }
    }
}

internal sealed partial class RavenLampPanelHooks
{
    private void AttachInteraction()
    {
        DetachInteraction();
        if (_player is null) return;
        RavenRetainedIconHooks.Track(_player);
        var target = _stage?.SacrificeTarget ?? _fallbackTarget;
        if (target is not null) _drag = new RavenNativeDragHooks(_player, this, target, CanSubmit, Submit);
        _input = new RavenPanelInputHooks(this, CanUseInput, () =>
        {
            if (_stage is not null) _stage.PickupHovered();
            else _fallbackPickup?.Invoke();
        }, RequestExit,
            _stage?.Hud.HasNativeBack == true);
    }

    private void DetachInteraction()
    {
        _drag?.Dispose();
        _drag = null;
        _input?.Dispose();
        _input = null;
    }

    private bool CanSubmit() => CanUseInput() && !IsTerminal && !RavenServiceHooks.State.Faulted
        && RavenServiceHooks.State.Pending is null && !RavenServiceHooks.Busy
        && (_stage is null || _stage.Phase == RavenStagePhase.Ready && !_stage.IsBusy);

    private bool CanUseInput() => !_closed && IsVisibleInTree() && RavenOverlayHooks.CanUsePanelInput(this)
        && NOverlayStack.Instance is { ScreenCount: > 0 } stack && ReferenceEquals(stack.Peek(), this)
        && ActiveScreenContext.Instance.IsCurrent(this)
        && NModalContainer.Instance?.OpenModal is null && NMapScreen.Instance?.IsOpen != true
        && NCapstoneContainer.Instance?.InUse != true && NGame.Instance?.Transition.InTransition != true
        && !NDevConsole.IsConsoleVisible && NGame.IsGameFocusedWindow();

    private void Submit(MegaCrit.Sts2.Core.Models.RelicModel relic, Vector2 point, long revision)
    {
        if (_stage is not null) _stage.Stake(relic, point, revision);
        else RunOperation(async () => { await RavenServiceHooks.SubmitStakeAsync(_player, relic, revision); });
    }
}

internal static partial class RavenOverlayHooks
{
    internal static bool CanUsePanelInput(RavenLampPanelHooks panel) => ReferenceEquals(_panel, panel)
        && !_entering && !_leaving && !_inPanelOperation;
}

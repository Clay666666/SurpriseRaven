using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace SurpriseRaven.Hooks.Raven;

internal sealed partial class RavenNativeDragHooks : IDisposable
{
    internal const float Threshold = 10f;
    private readonly Control _panel;
    private readonly RavenSacrificeTargetHooks _target;
    private readonly Func<bool> _allowed;
    private readonly Func<RelicModel, bool> _eligible;
    private readonly Func<(long Revision, long Opportunity)> _identity;
    private readonly Action<RelicModel, Vector2, long> _submit;
    private readonly Player? _player;
    private readonly TextureRect _ghost = new()
    {
        Name = "RavenDragPreview", Size = new Vector2(96, 96), Visible = false,
        MouseFilter = Control.MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
    };
    private Control? _previewLayer;
    private SceneTree? _tree;
    private NButton? _source;
    private RelicModel? _relic;
    private Vector2 _pressedAt;
    private (long Revision, long Opportunity) _started;
    private bool _disposed;
    internal bool Active => _source is not null;
    internal bool Dragging { get; private set; }
    internal Vector2 PreviewCenter => _ghost.GetGlobalTransformWithCanvas() * (_ghost.Size / 2);

    internal RavenNativeDragHooks(Player player, Control panel, RavenSacrificeTargetHooks target,
        Func<bool> allowed, Action<RelicModel, Vector2, long> submit)
        : this(panel, target, allowed,
            relic => player.Relics.Any(r => ReferenceEquals(r, relic)) && RavenStakeEligibilityHooks.CanStake(player, relic),
            () => (RavenServiceHooks.State.Revision, RavenServiceHooks.State.OpportunityId), submit)
    {
        _player = player;
        if (_previewLayer is not null && NRun.Instance?.GlobalUi.AboveTopBarVfxContainer is { } host)
        {
            _previewLayer.GetParent()?.RemoveChild(_previewLayer);
            host.AddChild(_previewLayer);
        }
        if (_tree is not null) RefreshSources();
    }

    // Injected inventory/identity allows real native input dispatch in an isolated SubViewport.
    internal RavenNativeDragHooks(Control panel, RavenSacrificeTargetHooks target, Func<bool> allowed,
        Func<RelicModel, bool> eligible, Func<(long Revision, long Opportunity)> identity,
        Action<RelicModel, Vector2, long> submit)
    {
        _panel = panel; _target = target; _allowed = allowed;
        _eligible = eligible; _identity = identity; _submit = submit;
        panel.TreeEntered += Attach;
        panel.TreeExiting += Detach;
        panel.VisibilityChanged += VisibilityChanged;
        if (panel.IsInsideTree()) Attach();
    }

    private bool CanUse() => !_disposed && GodotObject.IsInstanceValid(_panel)
        && !_panel.IsQueuedForDeletion() && _panel.IsInsideTree() && _panel.IsVisibleInTree()
        && GodotObject.IsInstanceValid(_target) && _target.IsInsideTree() && _target.IsVisibleInTree() && _allowed();

    private void Attach()
    {
        if (_disposed || _tree is not null) return;
        _tree = _panel.GetTree();
        _tree.ProcessFrame += Tick;
        _previewLayer = new Control { Name = "RavenDragLayer", MouseFilter = Control.MouseFilterEnum.Ignore };
        Node host = _player is not null ? NRun.Instance?.GlobalUi.AboveTopBarVfxContainer ?? _panel : _panel;
        host.AddChild(_previewLayer);
        _previewLayer.AddChild(_ghost);
        Tick();
    }

    private void Tick()
    {
        if (_disposed) return;
        if (!GodotObject.IsInstanceValid(_panel) || !GodotObject.IsInstanceValid(_target)) { Dispose(); return; }
        RefreshSources();
        bool available = CanUse();
        foreach (var binding in _bindings.Values) binding.SetHint(available && _eligible(binding.Model));
        if (Active && !SessionValid()) Cancel();
        _target.SetState(available, available && Dragging && _target.HitGlobal(_panel.GetGlobalMousePosition()));
        if (Dragging) PositionPreview(_panel.GetGlobalMousePosition());
    }

    private bool SessionValid() => CanUse() && _source is not null && _relic is not null
        && _bindings.TryGetValue(_source, out var binding) && ReferenceEquals(binding.Model, _relic)
        && SourceVisible(_source) && _source.IsEnabled && _eligible(_relic) && _started == _identity();

    private void Begin(NButton source, RelicModel relic, InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse
            || !CanUse() || !SourceVisible(source) || !source.IsEnabled || !_eligible(relic)) return;
        if (RavenNativeDragInputHooks.Owner is { } owner && !ReferenceEquals(owner, this)) owner.Cancel();
        Cancel();
        RavenNativeDragInputHooks.ClearSuppression();
        _source = source; _relic = relic; _pressedAt = mouse.GlobalPosition; _started = _identity();
        RavenNativeDragInputHooks.Owner = this;
    }

    internal void Receive(NButton source, InputEvent input)
    {
        if (!ReferenceEquals(_source, source)) return;
        if (!SessionValid()) { Cancel(); return; }
        if (input is InputEventMouseMotion motion)
        {
            if (!Dragging && motion.GlobalPosition.DistanceTo(_pressedAt) > Threshold)
            {
                Dragging = true;
                _ghost.Texture = _relic!.Icon;
                _ghost.Visible = true;
                RavenNativeDragInputHooks.ClearNativePress(source);
            }
            if (Dragging) PositionPreview(motion.GlobalPosition);
            _target.SetState(true, Dragging && _target.HitGlobal(motion.GlobalPosition));
        }
        else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mouse)
        {
            bool drop = Dragging && _target.HitGlobal(mouse.GlobalPosition);
            var relic = _relic!;
            long revision = _started.Revision;
            bool dragged = Dragging;
            Reset(clearPress: dragged || !source.GetGlobalRect().HasPoint(mouse.GlobalPosition));
            if (dragged) RavenNativeDragInputHooks.SuppressInspect(relic);
            if (drop)
            {
                Logger.Log($"[Raven] Native relic drop: {relic.Id}, revision={revision}.");
                _submit(relic, mouse.GlobalPosition, revision);
            }
        }
    }

    private void PositionPreview(Vector2 point)
    {
        if (_previewLayer is null) return;
        _ghost.Position = _previewLayer.GetGlobalTransformWithCanvas().AffineInverse() * point - _ghost.Size / 2;
    }

    internal bool Cancel()
    {
        bool active = Active;
        if (_relic is not null && active) RavenNativeDragInputHooks.SuppressInspect(_relic);
        Reset(clearPress: true);
        return active;
    }

    private void Reset(bool clearPress)
    {
        if (clearPress && _source is not null && GodotObject.IsInstanceValid(_source))
            RavenNativeDragInputHooks.ClearNativePress(_source);
        _source = null; _relic = null; Dragging = false;
        if (GodotObject.IsInstanceValid(_ghost)) _ghost.Visible = false;
        if (GodotObject.IsInstanceValid(_target)) _target.SetState(CanUse());
        if (ReferenceEquals(RavenNativeDragInputHooks.Owner, this)) RavenNativeDragInputHooks.Owner = null;
    }

    private void VisibilityChanged() { if (!_panel.IsVisibleInTree()) Cancel(); }

    private void Detach()
    {
        Cancel();
        if (_tree is not null && GodotObject.IsInstanceValid(_tree)) _tree.ProcessFrame -= Tick;
        _tree = null;
        ReleaseSources();
        if (_previewLayer is not null && GodotObject.IsInstanceValid(_previewLayer))
        {
            _previewLayer.RemoveChild(_ghost);
            _previewLayer.QueueFree();
        }
        _previewLayer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
        if (GodotObject.IsInstanceValid(_panel))
        {
            _panel.TreeEntered -= Attach;
            _panel.TreeExiting -= Detach;
            _panel.VisibilityChanged -= VisibilityChanged;
        }
        if (GodotObject.IsInstanceValid(_ghost)) _ghost.Free();
    }
}

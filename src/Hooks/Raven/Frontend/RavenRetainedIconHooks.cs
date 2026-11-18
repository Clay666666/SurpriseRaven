using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

// Pending ownership, rather than an overlay's lifetime, owns the temporary inventory visual.
internal sealed class RavenRetainedIconHooks : IDisposable
{
    private static RavenRetainedIconHooks? _shared;
    private static Player? _sharedPlayer;
    private readonly Func<RelicModel?> _retained;
    private readonly Func<RelicModel, Control?> _visual;
    private readonly Func<bool> _active;
    private readonly SceneTree _tree;
    private readonly bool _disposeWhenResolved;
    private Control? _hidden;
    private NRelicInventoryHolder? _hiddenOwner;
    private bool _wasVisible, _ownerWasEnabled, _disposed;

    internal static void Track(Player player)
    {
        if (_shared is not null && !ReferenceEquals(_sharedPlayer, player)) _shared.Dispose();
        if (_shared is null && RavenServiceHooks.GetRetainedStakeForDisplay(player) is not null
            && Engine.GetMainLoop() is SceneTree tree)
        {
            _sharedPlayer = player;
            _shared = new RavenRetainedIconHooks(tree, () => RavenServiceHooks.GetRetainedStakeForDisplay(player), FindVisual,
                () => ReferenceEquals(RavenServiceHooks.CurrentPlayer, player)
                    && RunManager.Instance.IsInProgress && !RunManager.Instance.IsCleaningUp, true);
        }
        _shared?.Refresh();
    }

    // Detached fixtures can supply native holders and pending identity without starting a run.
    internal RavenRetainedIconHooks(Control host, Func<RelicModel?> retained, Func<RelicModel, Control?> visual)
        : this(host.GetTree(), retained, visual,
            () => GodotObject.IsInstanceValid(host) && host.IsInsideTree(), false) { Refresh(); }

    private RavenRetainedIconHooks(SceneTree tree, Func<RelicModel?> retained,
        Func<RelicModel, Control?> visual, Func<bool> active, bool disposeWhenResolved)
    {
        _retained = retained; _visual = visual; _active = active;
        _tree = tree; _disposeWhenResolved = disposeWhenResolved;
        _tree.ProcessFrame += Refresh;
    }

    private static Control? FindVisual(RelicModel relic) => NRun.Instance?.GlobalUi.RelicInventory.RelicNodes
        .FirstOrDefault(holder => GodotObject.IsInstanceValid(holder) && !holder.IsQueuedForDeletion()
            && holder.IsNodeReady() && ReferenceEquals(holder.Relic.Model, relic))?.Relic;

    internal void Refresh()
    {
        if (_disposed) return;
        var original = _active() ? _retained() : null;
        var visual = original is null ? null : _visual(original);
        if (visual is not null && (!GodotObject.IsInstanceValid(visual) || visual.IsQueuedForDeletion())) visual = null;
        if (!ReferenceEquals(visual, _hidden))
        {
            Restore();
            if (visual is not null)
            {
                _hidden = visual;
                _wasVisible = visual.Visible;
                for (Node? parent = visual.GetParent(); parent is not null; parent = parent.GetParent())
                    if (parent is NRelicInventoryHolder holder)
                    {
                        _hiddenOwner = holder;
                        _ownerWasEnabled = holder.IsEnabled;
                        holder.ReleaseFocus();
                        holder.SetEnabled(false);
                        NHoverTipSet.Remove(holder); // The holder, not its hidden NRelic, owns the tip.
                        break;
                    }
                visual.Hide(); // Holder and its inventory flow-layout slot remain intact.
            }
        }
        if (original is null && _disposeWhenResolved) Dispose();
    }

    private void Restore()
    {
        if (_hidden is not null && GodotObject.IsInstanceValid(_hidden) && !_hidden.IsQueuedForDeletion())
            _hidden.Visible = _wasVisible;
        if (_hiddenOwner is not null && GodotObject.IsInstanceValid(_hiddenOwner) && !_hiddenOwner.IsQueuedForDeletion())
            _hiddenOwner.SetEnabled(_ownerWasEnabled);
        _hiddenOwner = null;
        _hidden = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(_tree)) _tree.ProcessFrame -= Refresh;
        Restore();
        if (ReferenceEquals(_shared, this)) { _shared = null; _sharedPlayer = null; }
    }
}

using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace SurpriseRaven.Hooks.Raven;

internal sealed partial class RavenNativeDragHooks
{
    private readonly Dictionary<NButton, RavenNativeDragBindingHooks> _bindings = [];
    private NRelicInventory? _inventory;
    internal int SourceCount => _bindings.Count;

    private void RefreshSources()
    {
        if (_player is null) return;
        var inventory = NRun.Instance?.GlobalUi.RelicInventory;
        if (inventory is not null && (!GodotObject.IsInstanceValid(inventory) || inventory.IsQueuedForDeletion())) inventory = null;
        if (!ReferenceEquals(_inventory, inventory))
        {
            ReleaseSources();
            _inventory = inventory;
            if (_inventory is not null) _inventory.RelicsChanged += RefreshSources;
        }
        if (_inventory is null) return;
        var sources = _inventory.RelicNodes.Where(h => GodotObject.IsInstanceValid(h) && !h.IsQueuedForDeletion()
            && h.IsNodeReady() && h.Relic is not null).Select(h => ((NButton)h, h.Relic.Model)).ToArray();
        SetSources(sources);
    }

    internal void SetSources(IEnumerable<(NButton Button, RelicModel Model)> sources)
    {
        if (_disposed) return;
        var live = sources.ToDictionary(pair => pair.Button, pair => pair.Model);
        foreach (var (source, binding) in _bindings.ToArray())
            if (!live.TryGetValue(source, out var model) || !ReferenceEquals(model, binding.Model))
            {
                if (ReferenceEquals(source, _source)) Cancel();
                binding.Dispose();
                _bindings.Remove(source);
            }
        foreach (var (source, model) in live)
            if (!_bindings.ContainsKey(source))
                _bindings.Add(source, new RavenNativeDragBindingHooks(source, model,
                    input => Begin(source, model, input), () => { if (ReferenceEquals(source, _source)) Cancel(); }));
    }

    private void ReleaseSources()
    {
        Cancel();
        if (_inventory is not null && GodotObject.IsInstanceValid(_inventory)) _inventory.RelicsChanged -= RefreshSources;
        _inventory = null;
        foreach (var binding in _bindings.Values) binding.Dispose();
        _bindings.Clear();
    }

    private static bool SourceVisible(Control source) => GodotObject.IsInstanceValid(source)
        && !source.IsQueuedForDeletion() && source.IsInsideTree() && source.IsVisibleInTree()
        && source.Size.X > 0 && source.Size.Y > 0 && source.GetGlobalRect().Intersects(source.GetViewportRect());
}

// Own only temporary hint/threshold state; never disable or reparent the game's holder.
internal sealed class RavenNativeDragBindingHooks : IDisposable
{
    private readonly NButton _button;
    private readonly float _previousThreshold;
    private readonly NClickableControl.MousePressedEventHandler _pressed;
    private readonly Action _exit;
    private readonly RavenRelicGlowHooks _hint;
    private bool _disposed;
    internal RelicModel Model { get; }

    internal RavenNativeDragBindingHooks(NButton button, RelicModel model, Action<InputEvent> pressed, Action exit)
    {
        _button = button; Model = model;
        _pressed = input => pressed(input); _exit = exit;
        _previousThreshold = RavenNativeDragInputHooks.GetThreshold(button);
        RavenNativeDragInputHooks.SetThreshold(button, RavenNativeDragHooks.Threshold);
        _hint = new RavenRelicGlowHooks(button is NRelicInventoryHolder holder ? holder.Relic : button);
        button.MousePressed += _pressed;
        button.TreeExiting += _exit;
    }

    internal void SetHint(bool visible)
    {
        _hint.SetVisible(visible);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(_button))
        {
            _button.MousePressed -= _pressed;
            _button.TreeExiting -= _exit;
            if (RavenNativeDragInputHooks.GetThreshold(_button) == RavenNativeDragHooks.Threshold)
                RavenNativeDragInputHooks.SetThreshold(_button, _previousThreshold);
        }
        _hint.Dispose();
    }
}

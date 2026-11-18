using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;
using static SurpriseRaven.Hooks.Raven.RavenStyleHooks;

namespace SurpriseRaven.Hooks.Raven;

// A standalone holder has no treasure collection or synchronizer; only our callback can collect.
internal sealed partial class RavenRewardSlotHooks : IDisposable
{
    private readonly RelicModel _relic;
    private readonly IRunState? _runState;
    private readonly Action _pickup;
    private readonly Action<bool> _hover;
    private NTreasureRoomRelicHolder? _native;
    private TextureButton? _fallback;
    private RavenRelicGlowHooks? _glow;
    private bool _interactive, _initialized, _disposed, _readySubscribed;

    internal Control Root { get; } = new() { Size = new Vector2(136, 136), MouseFilter = Control.MouseFilterEnum.Ignore };
    internal Control Button => (Control?)_native ?? _fallback!;
    internal bool Interactive
    {
        set
        {
            _interactive = value;
            if (_native is not null && _initialized) _native.SetEnabled(value);
            if (_fallback is not null) _fallback.Disabled = !value;
            if (!value) ClearHover();
            else RefreshFallbackHover();
        }
    }

    internal RavenRewardSlotHooks(RelicModel relic, IRunState? runState, Action pickup, Action<bool> hover,
        Func<NTreasureRoomRelicHolder?>? nativeFactory)
    {
        _relic = relic;
        _runState = runState;
        _pickup = pickup;
        _hover = hover;
        Root.VisibilityChanged += OnVisibilityChanged;
        try
        {
            if (runState is not null)
                _native = nativeFactory is null ? Native<NTreasureRoomRelicHolder>("res://scenes/ui/treasure_relic_holder.tscn") : nativeFactory();
            if (_native is not null)
            {
                _native.Ready += InitializeNative;
                _readySubscribed = true;
                _native.Released += OnReleased;
                _native.Focused += OnFocused;
                _native.Unfocused += OnUnfocused;
                _native.MouseExited += OnNativeMouseExited;
                Root.AddChild(_native);
                return;
            }
        }
        catch (Exception error) { Logger.Log($"[Raven] Chest relic construction failed: {error}"); }
        DetachNative();
        CreateFallback();
    }

    private void InitializeNative()
    {
        if (_disposed || _native is null) return;
        DisconnectReady();
        try
        {
            // 111 places existing glows with a global 68px offset; retain their scene-local
            // centers under the lamp's Y compression. 113 creates correctly local glows.
            var glowPositions = new[] { "%UncommonGlow", "%RareGlow" }
                .Select(path => _native.GetNodeOrNull<Node2D>(path)).OfType<Node2D>()
                .Select(node => (Node: node, node.Position)).ToArray();
            _native.Initialize(_relic, _runState!);
            foreach (var glow in glowPositions) glow.Node.Position = glow.Position;
            RavenRelicGlowHooks.SuppressNativeRarityGlows(_native.Relic);
            _glow = new RavenRelicGlowHooks(_native.Relic);
            _glow.SetVisible(true);
            _native.VoteContainer.Hide();
            _initialized = true;
            _native.SetEnabled(_interactive);
        }
        catch (Exception error)
        {
            Logger.Log($"[Raven] Chest relic initialization failed; retaining clickable fallback: {error}");
            DetachNative();
            CreateFallback();
        }
    }

    private void CreateFallback()
    {
        _fallback = new TextureButton
        {
            TextureNormal = _relic.BigIcon, IgnoreTextureSize = true, StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            Size = Root.Size, PivotOffset = Root.Size / 2, FocusMode = Control.FocusModeEnum.All, Disabled = !_interactive
        };
        _fallback.Pressed += OnFallbackPressed;
        _fallback.MouseEntered += OnFallbackMouseEntered;
        _fallback.FocusEntered += RefreshFallbackHover;
        _fallback.MouseExited += OnFallbackMouseExited;
        _fallback.FocusExited += RefreshFallbackHover;
        Root.AddChild(_fallback);
        _glow = new RavenRelicGlowHooks(_fallback);
        _glow.SetVisible(true);
        if (_runState is not null) Logger.Log($"[Raven] Native chest holder unavailable for {_relic.Id}; using pickup fallback.");
    }

    private void OnReleased(NClickableControl _) { if (_interactive && !_disposed) _pickup(); }
    private void OnFocused(NClickableControl _) { if (!_disposed) _hover(true); }
    private void OnUnfocused(NClickableControl _) { if (!_disposed) _hover(false); }
    private void OnFallbackPressed() { if (_interactive && !_disposed) _pickup(); }

    private void DetachNative()
    {
        if (_native is null) return;
        _glow?.Dispose();
        _glow = null;
        DisconnectReady();
        _native.Released -= OnReleased;
        _native.Focused -= OnFocused;
        _native.Unfocused -= OnUnfocused;
        _native.MouseExited -= OnNativeMouseExited;
        NHoverTipSet.Remove(_native);
        _native.Hide();
        _native.MouseFilter = Control.MouseFilterEnum.Ignore;
        _native.QueueFree();
        _native = null;
        _initialized = false;
    }

    private void DisconnectReady()
    {
        if (!_readySubscribed || _native is null) return;
        _native.Ready -= InitializeNative;
        _readySubscribed = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearHover();
        Root.VisibilityChanged -= OnVisibilityChanged;
        _glow?.Dispose();
        DetachNative();
        if (_fallback is not null)
        {
            NHoverTipSet.Remove(_fallback);
            _fallback.Pressed -= OnFallbackPressed;
            _fallback.MouseEntered -= OnFallbackMouseEntered;
            _fallback.FocusEntered -= RefreshFallbackHover;
            _fallback.MouseExited -= OnFallbackMouseExited;
            _fallback.FocusExited -= RefreshFallbackHover;
            _fallback.Disabled = true;
        }
        Root.QueueFree();
    }
}

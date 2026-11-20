using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace SurpriseRaven.Hooks.Raven;

internal sealed partial class RavenLampPanelHooks : Control, IOverlayScreen
{
    private readonly Player _player;
    private readonly VBoxContainer _content = new();
    private readonly Control _pageRoot = new()
    {
        Name = "RavenPageContent", Size = RavenStyleHooks.Design,
        MouseFilter = MouseFilterEnum.Ignore, ClipContents = true
    };
    private readonly RavenStageHooks? _stage;
    private readonly Action<string> _playAudio;
    private Control _exit = null!;
    private bool _closed;
    private readonly string? _terminalMessage;
    private readonly bool _restoredSwallow;
    private long _revision;
    private RavenNativeDragHooks? _drag;
    private RavenPanelInputHooks? _input;
    private RavenSacrificeTargetHooks? _fallbackTarget;
    private Action? _fallbackPickup;
    internal bool IsTerminal => _terminalMessage is not null || (_player is not null && !RavenServiceHooks.State.HasOpportunity);
    internal RavenStageHooks? Stage => _stage;
    internal Control ContentRoot => _pageRoot;

    public NetScreenType ScreenType => NetScreenType.Rewards;
    public bool UseSharedBackstop => true;
    public Control DefaultFocusedControl => _stage?.DefaultFocus ?? _exit;
    public Control FocusedControlFromTopBar => DefaultFocusedControl;

    internal RavenLampPanelHooks(Player player, string? terminalMessage = null, bool allowStage = true,
        Action<string>? playAudio = null, bool restoredSwallow = false)
    {
        _player = player;
        _terminalMessage = terminalMessage;
        _restoredSwallow = restoredSwallow;
        _playAudio = playAudio ?? RavenAudioHooks.Play;
        Name = "RavenLampPanel";
        Theme = new Theme { DefaultFont = FontManager.GetSubstituteFont("zhs", FontType.Regular), DefaultFontSize = 20 };
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_pageRoot);
        TreeEntered += StartContentLayout;
        TreeExiting += StopContentLayout;
        Resized += UpdateContentLayout;
        TreeEntered += AttachInteraction;
        TreeExiting += DetachInteraction;
        if (allowStage && player is not null)
        {
            try
            {
                _stage = new RavenStageHooks(player, terminalMessage, CanUseInput, _playAudio);
                _stage.ExitRequested += RequestExit;
                _pageRoot.AddChild(_stage);
                return;
            }
            catch (Exception error)
            {
                Logger.Log($"[Raven] Stage unavailable, using text fallback: {error}");
                _stage = null;
            }
        }
        BuildFallbackFrame();
        Refresh();
    }

    internal void Refresh()
    {
        if (_closed) return;
        if (_player is not null) RavenRetainedIconHooks.Track(_player);
        if (_stage is not null) { _stage.Sync(); return; }
        _fallbackPickup = null;
        if (_fallbackTarget is not null) _fallbackTarget.GetParent()?.RemoveChild(_fallbackTarget);
        foreach (var child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }
        BuildContent();
        RavenFocusHooks.FocusDefault(this, _exit);
    }

    public void AfterOverlayOpened()
    {
        if (_restoredSwallow) return;
        _playAudio("entry");
        if (_player is not null) RavenReplayLogHooks.Mark(_player, "lamp-open");
    }
    public void AfterOverlayShown()
    {
        Show();
        if (_player is not null) RavenRetainedIconHooks.Track(_player);
        UpdateContentLayout();
        _stage?.OnShown();
        RavenFocusHooks.FocusDefault(this, DefaultFocusedControl);
        _input?.Refresh();
    }
    public void AfterOverlayHidden()
    {
        _stage?.OnHidden();
        _drag?.Cancel();
        Hide();
        _input?.Refresh();
    }
    public void AfterOverlayClosed()
    {
        if (_closed) return;
        _closed = true;
        StopContentLayout();
        DetachInteraction();
        _stage?.Shutdown();
        if (_player is not null) RavenRetainedIconHooks.Track(_player);
        if (_fallbackTarget is not null && _fallbackTarget.GetParent() is null) _fallbackTarget.Free();
        RavenOverlayHooks.Closed(this);
        GetParent()?.RemoveChild(this);
        QueueFree();
    }

    internal void RequestExit()
    {
        if (!CanUseInput()) return;
        if (_drag?.Cancel() == true) return;
        if (_stage?.IsBusy == true)
        {
            _stage.Hud.Toast("渡鸦还在路上……");
            return;
        }
        if (RavenServiceHooks.Busy) return;
        long revision = RavenServiceHooks.State.Revision;
        RavenOverlayHooks.ExitWithTransition(IsTerminal ? () => Task.CompletedTask
            : () => RavenServiceHooks.ExitAsync(_player, revision));
    }

    private void RunOperation(Func<Task> operation)
    {
        if (CanUseInput()) _ = RavenOverlayHooks.RunServiceOperation(operation);
    }
}

using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using static SurpriseRaven.Hooks.Raven.RavenStyleHooks;

namespace SurpriseRaven.Hooks.Raven;

// Native reward-screen banner and back button where available, plus the current round.
internal sealed partial class RavenHudHooks : Control
{
    private const float BannerScale = 0.91f;
    private const float InfoPanelTop = 36f;
    private const float RelicClearance = 12f;
    private readonly NCommonBanner? _banner;
    private readonly NBackButton? _back;
    private readonly Label _info, _message, _caption, _pop, _toast;
    private readonly PanelContainer _panel;
    private Tween? _popTween, _toastTween;

    internal RavenHudHooks()
    {
        Name = "Hud";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _banner = Native<NCommonBanner>("res://scenes/ui/common_banner.tscn");
        if (_banner is not null)
        {
            AddChild(RavenNativeLayoutHooks.Mount(_banner, new Vector2(0.5f, 0),
                new Vector2(-_banner.CustomMinimumSize.X * BannerScale / 2, 0), BannerScale));
        }
        else AddChild(Centered(Text("渡鸦路灯", LampTitleSize(44), bold: true), 8));
        // Sized to its content: a short round plaque, widening only when a message needs it.
        var panel = _panel = new PanelContainer { Position = new Vector2(36, InfoPanelTop) };
        panel.AddThemeStyleboxOverride("panel", Panel());
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        _info = Text("", 26, bold: true);
        _message = Text("", 20, outline: 5);
        _message.Hide();
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _message.CustomMinimumSize = new Vector2(420, 0);
        column.AddChild(_info);
        column.AddChild(_message);
        panel.AddChild(column);
        AddChild(panel);
        Resized += FitRoundPanel;
        _back = Native<NBackButton>("res://scenes/ui/back_button.tscn");
        if (_back is not null)
        {
            _back.Released += _ => ExitRequested?.Invoke();
            AddChild(RavenNativeLayoutHooks.Mount(_back, new Vector2(0, 1), new Vector2(36, -150)));
        }
        else
        {
            var exit = RavenStyleHooks.Button("离开（Esc）", () => ExitRequested?.Invoke());
            exit.AnchorTop = exit.AnchorBottom = 1;
            exit.OffsetLeft = 36; exit.OffsetRight = 260; exit.OffsetTop = -100; exit.OffsetBottom = -40;
            AddChild(exit);
            ExitFallback = exit;
        }
        _caption = Centered(Text("", 20, outline: 5), -50, bottom: true);
        _caption.Hide();
        _toast = Centered(Text("", 24, bold: true), -100, bottom: true);
        _toast.Modulate = Colors.Transparent;
        _pop = Centered(Text("", 64, bold: true, outline: 14), 410);
        _pop.Modulate = Colors.Transparent;
        AddChild(_caption);
        AddChild(_toast);
        AddChild(_pop);
    }

    internal event Action? ExitRequested;
    internal Button? ExitFallback { get; }
    internal Control ExitControl => (Control?)_back ?? ExitFallback!;
    internal bool HasNativeBack => _back is not null;
    internal Control? Banner => _banner;
    internal float BannerHeight => (_banner?.Size.Y ?? 0) * BannerScale;
    internal Control InfoPanel => _panel;
    internal Label CaptionLabel => _caption;
    internal Label PopLabel => _pop;

    internal void PlaceInfoBelowRelics(float? relicBottom)
    {
        float top = Math.Max(InfoPanelTop, relicBottom.GetValueOrDefault() + RelicClearance);
        if (!Mathf.IsEqualApprox(_panel.Position.Y, top))
            _panel.Position = new Vector2(_panel.Position.X, top);
    }

    internal void OnShown()
    {
        try
        {
            if (_banner is not null)
            {
                if (Font(true) is { } font) _banner.label.AddThemeFontOverride("font", font);
                _banner.ChangeText("渡鸦路灯");
                _banner.AnimateIn();
            }
            _back?.Enable();
        }
        catch (Exception error) { Logger.Log($"[Raven] Native HUD widgets failed: {error.Message}"); }
    }

    internal void OnHidden()
    {
        try { _back?.Disable(); } catch { }
    }

    internal void SetInfo(int round, int lights, bool ended)
    {
        _info.Text = ended ? "渡鸦路灯已结束" : $"第 {round + 1} 轮";
        _panel.ResetSize();
    }

    internal void SetMessage(string text)
    {
        _message.Text = text;
        _message.Visible = !string.IsNullOrWhiteSpace(text);
        FitRoundPanel();
    }

    private void FitRoundPanel()
    {
        float bannerLeft = Size.X / 2 - (_banner?.CustomMinimumSize.X ?? 0) * BannerScale / 2;
        float margins = _panel.GetThemeStylebox("panel").GetMinimumSize().X;
        float wrapWidth = Mathf.Clamp(bannerLeft - _panel.Position.X - margins - 12f, 140f, 420f);
        _message.CustomMinimumSize = new Vector2(wrapWidth, 0);
        _panel.ResetSize();
    }

    internal void SetCaption(string text)
    {
        _caption.Text = text;
        _caption.Visible = !string.IsNullOrWhiteSpace(text);
    }

    internal void Pop(string text, Color color, bool animate = true)
    {
        _pop.Text = text;
        _pop.AddThemeColorOverride("font_color", color);
        _pop.PivotOffset = _pop.Size / 2;
        _popTween?.Kill();
        _popTween = _pop.CreateTween();
        _pop.Scale = Vector2.One * (animate ? 0.6f : 1f);
        _popTween.TweenProperty(_pop, "modulate:a", 1f, animate ? 0.15 : 0.3);
        if (animate) _popTween.Parallel().TweenProperty(_pop, "scale", Vector2.One, 0.35).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _popTween.TweenInterval(1.6);
        _popTween.TweenProperty(_pop, "modulate:a", 0f, 0.5);
    }

    internal void Toast(string text)
    {
        _toast.Text = text;
        _toastTween?.Kill();
        _toastTween = _toast.CreateTween();
        _toastTween.TweenProperty(_toast, "modulate:a", 1f, 0.12);
        _toastTween.TweenInterval(1.8);
        _toastTween.TweenProperty(_toast, "modulate:a", 0f, 0.4);
    }

    private static Label Centered(Label label, float y, bool bottom = false)
    {
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AnchorRight = 1;
        if (bottom) { label.AnchorTop = label.AnchorBottom = 1; }
        label.OffsetTop = y;
        label.OffsetBottom = bottom ? Math.Min(0, y + 80) : y + 80;
        return label;
    }
}

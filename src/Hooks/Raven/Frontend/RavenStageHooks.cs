using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using static SurpriseRaven.Hooks.Raven.RavenStyleHooks;

namespace SurpriseRaven.Hooks.Raven;

// The lamp scene: backdrop, lamp, ten candles, raven, native rewards and sacrifice target.
// Everything is laid out in 1920x1080 design space and scaled to fit the viewport.
internal sealed partial class RavenStageHooks : Control
{
    private readonly Player _player;
    private readonly Func<bool> _canInteract;
    private readonly Action<string> _playAudio;
    private readonly string? _terminal;
    private readonly Control _canvas = new() { Name = "Canvas", Size = Design, MouseFilter = MouseFilterEnum.Ignore };
    private readonly List<RavenCandleHooks> _candles = [];
    private readonly RavenRewardsHooks _rewards;
    private readonly RavenBirdHooks _bird = new();
    private readonly Control _fx = new() { Name = "Fx", Size = Design, MouseFilter = MouseFilterEnum.Ignore };
    private double _time;
    private bool _subscribed;
    private readonly long _generation = RavenServiceHooks.Generation;

    internal RavenStageHooks(Player player, string? terminal, Func<bool>? canInteract = null,
        Action<string>? playAudio = null)
    {
        _player = player;
        _canInteract = canInteract ?? (() => true);
        _playAudio = playAudio ?? RavenAudioHooks.Play;
        _rewards = new RavenRewardsHooks(player.RunState) { CanInteract = _canInteract };
        _terminal = terminal;
        Name = "RavenStage";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddBackdrop();
        _canvas.AddChild(new RavenLampArtHooks());
        _canvas.AddChild(SacrificeTarget);
        for (int i = 0; i < RavenLayout.Candles.Length; i++)
        {
            var candle = new RavenCandleHooks(RavenLayout.Candles[i], i);
            _candles.Add(candle);
            _canvas.AddChild(candle);
        }
        _canvas.AddChild(_rewards);
        _canvas.AddChild(_bird);
        _canvas.AddChild(_fx);
        AddChild(_canvas);
        AddChild(Hud);
        Hud.ExitRequested += () => ExitRequested?.Invoke();
        _rewards.PickupRequested += Pickup;
        _bird.ReducedMotion = RavenMotionPrefsHooks.Reduced;
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            tree.ProcessFrame += Tick;
            _subscribed = true;
        }
        Sync();
    }

    internal event Action? ExitRequested;
    internal RavenHudHooks Hud { get; } = new();
    internal IReadOnlyList<RavenCandleHooks> Candles => _candles;
    internal RavenRewardsHooks Rewards => _rewards;
    internal RavenSacrificeTargetHooks SacrificeTarget { get; } = new();
    internal Control DefaultFocus => _rewards.FirstSlot ?? Hud.ExitControl;
    internal Vector2 CanvasPoint(Vector2 viewportPoint) => _canvas.GetGlobalTransformWithCanvas().AffineInverse() * viewportPoint;

    internal RavenBirdHooks Bird => _bird;
    internal Control Canvas => _canvas;
    internal bool ReducedMotion => _bird.ReducedMotion;

    internal void OnShown()
    {
        _bird.ReducedMotion = RavenMotionPrefsHooks.Reduced;
        Hud.OnShown();
    }

    internal void OnHidden()
    {
        _rewards.ClearHoverTips();
        Hud.OnHidden();
    }

    internal void Shutdown()
    {
        Hud.OnHidden();
        if (_subscribed && Engine.GetMainLoop() is SceneTree tree) tree.ProcessFrame -= Tick;
        _subscribed = false;
        if (_generation == RavenServiceHooks.Generation) RavenStageSession.TimelineActive = false;
        _timeline = null;
        _rewards.Clear();
    }

    private void Tick()
    {
        if (_generation != RavenServiceHooks.Generation || !GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;
        double delta = Math.Min(GetProcessDeltaTime(), 0.1);
        _time += delta;
        // The page is compressed vertically below the native top bar; counter it so the art stays round.
        var global = GetGlobalTransform().Scale;
        float squash = Mathf.Abs(global.X) > 1e-4f ? Mathf.Clamp(Mathf.Abs(global.Y / global.X), 0.05f, 20f) : 1f;
        float scale = Mathf.Min(Size.X / Design.X, Size.Y * squash / Design.Y);
        _canvas.Scale = new Vector2(scale, scale / squash);
        _canvas.Position = (Size - Design * _canvas.Scale) / 2;
        foreach (var candle in _candles) candle.Update(_time, delta);
        _bird.Update(_time, delta);
        _rewards.Update(_time);
        if (_timeline is not null && _plan is { } plan)
            foreach (var cue in _timeline.Advance(delta)) OnCue(cue, plan);
    }

    // Gameplay candles 0-5 follow the light count; 6-9 stay lit until the raven extinguishes all.
    internal void SetCandles(int lights)
    {
        for (int i = 0; i < _candles.Count; i++)
            _candles[i].SetLit(i >= 6 || i < lights, false);
    }

    private void IgniteEarnedCandles(RavenRoundPlan plan)
    {
        for (int i = Math.Max(0, plan.LightsBefore); i < Math.Min(6, plan.LightsAfter); i++)
        {
            var candle = _candles[i];
            if (candle.Lit) continue;
            candle.SetLit(true, true);
            _playAudio("candle.light");
        }
    }

    internal void ExtinguishAll(bool playSound = false)
    {
        bool extinguished = false;
        foreach (var candle in _candles.Where(candle => candle.Lit))
        {
            candle.SetLit(false, true);
            extinguished = true;
        }
        if (playSound && extinguished) _playAudio("candle.out");
    }

    private void AddBackdrop()
    {
        AddChild(new ColorRect { Color = new Color(0.04f, 0.05f, 0.09f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        if (RavenAssetsHooks.Texture("tex.sky") is { } sky)
            AddChild(new TextureRect
            {
                Texture = new AtlasTexture { Atlas = sky, Region = new Rect2(1024, 380, 2048, 1152) },
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                Modulate = new Color(0.62f, 0.6f, 0.72f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1,
                TextureFilter = TextureFilterEnum.LinearWithMipmaps
            });
        AddChild(new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Width = 256, Height = 144, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.55f), FillTo = new Vector2(1.05f, 0.55f),
                Gradient = new Gradient { Offsets = [0f, 0.55f, 1f], Colors = [new Color(0, 0, 0, 0), new Color(0.02f, 0.02f, 0.04f, 0.35f), new Color(0.01f, 0.01f, 0.02f, 0.9f)] }
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1
        });
    }
}

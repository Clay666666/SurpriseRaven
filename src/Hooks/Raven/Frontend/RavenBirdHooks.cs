using Godot;

namespace SurpriseRaven.Hooks.Raven;

// Painted raven node. The engine-free RavenBirdMotion drives position and pose; RavenBirdArtHooks
// paints the same geometry the offline preview renders.
internal sealed partial class RavenBirdHooks : Node2D
{
    private static readonly Color Haze = new(0.6f, 0.65f, 0.8f);
    private readonly RavenBirdMotion _motion = new(RavenLayout.Perch);
    private readonly Sprite2D _carry = new() { Visible = false, Position = RavenBirdArtHooks.CarryPoint };
    private float _carryScale = 1;
    private Vector2 _pupilOffset;

    internal RavenBirdHooks()
    {
        Name = "Raven";
        AddChild(_carry);
        Draw += () => RavenBirdArtHooks.Paint(new RavenPen(new RavenCanvasPainter(this)), _motion.Pose, _pupilOffset);
        Apply();
    }

    internal bool Flying => _motion.Flying;
    internal bool Settled => _motion.Settled;
    internal RavenBirdMotion Motion => _motion;
    internal bool ReducedMotion { get => _motion.ReducedMotion; set => _motion.ReducedMotion = value; }
    internal Vector2 BeakPosition => _motion.Transform * RavenBirdArtHooks.BeakTip;

    internal void Perch() { _motion.Perch(); Apply(); }
    internal void Away() { _motion.Away(); Apply(); }
    internal void Caw() => _motion.Caw();
    internal void Anticipate(double duration) => _motion.Anticipate(duration);

    internal void Fly(IReadOnlyList<RavenRoutePoint> route, double duration, int anchorIndex = -1, float anchorAt = 0)
    {
        _motion.Fly(route, duration, anchorIndex, anchorAt);
        Apply();
    }

    internal void Carry(Texture2D? icon)
    {
        _carry.Texture = icon;
        _carry.Visible = icon is not null;
        if (icon is not null) _carryScale = 56f / Math.Max(1, icon.GetWidth());
        Apply();
    }

    internal void Update(double time, double delta)
    {
        _motion.Update(delta);
        Apply();
        if (!IsVisibleInTree()) return;

        Vector2? pointer = null;
        if (DisplayServer.HasFeature(DisplayServer.Feature.Mouse) && GetWindow().HasFocus())
        {
            var mouse = GetViewport().GetMousePosition();
            if (GetViewportRect().HasPoint(mouse)) pointer = mouse;
        }
        var headToViewport = GetGlobalTransformWithCanvas() * RavenBirdArtHooks.HeadTransform(_motion.Pose.Head);
        _pupilOffset = RavenBirdEyeHooks.Offset(headToViewport, pointer);
        QueueRedraw();
    }

    private void Apply()
    {
        Visible = _motion.Visible;
        Position = _motion.Position;
        Rotation = _motion.Rotation;
        var scale = _motion.Scale;
        // Keep the transform invertible while the bird turns edge-on.
        Scale = new Vector2(Mathf.Abs(scale.X) < 0.02f ? 0.02f * (scale.X < 0 ? -1 : 1) : scale.X, scale.Y);
        float haze = Mathf.Clamp((1 - _motion.Depth) / 0.6f, 0, 1) * 0.55f;
        Modulate = new Color(Colors.White.Lerp(Haze, haze), _motion.Alpha);
        // The carried relic hangs from the beak but never reads mirrored.
        _carry.Scale = new Vector2(Mathf.Sign(Scale.X) * _carryScale, _carryScale);
    }
}

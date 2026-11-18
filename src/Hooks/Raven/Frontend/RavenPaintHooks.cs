using Godot;

namespace SurpriseRaven.Hooks.Raven;

// Engine-free drawing surface. The game draws through CanvasItem; the offline preview records
// the same calls, so both render identical geometry. Polygons must be simple (non-crossing).
internal interface IRavenPainter
{
    void Fill(Vector2[] points, Color color);
    void Shade(Vector2[] points, Color[] colors);
    void Stroke(Vector2[] points, Color color, float width, bool closed);
}

internal sealed class RavenCanvasPainter(CanvasItem item) : IRavenPainter
{
    public void Fill(Vector2[] points, Color color) { if (RavenPen.Drawable(points)) item.DrawColoredPolygon(points, color); }
    public void Shade(Vector2[] points, Color[] colors) { if (RavenPen.Drawable(points)) item.DrawPolygon(points, colors); }
    public void Stroke(Vector2[] points, Color color, float width, bool closed)
    {
        if (points.Length >= 2) item.DrawPolyline(closed ? [.. points, points[0]] : points, color, width, true);
    }
}

// A painter plus a local transform, so painted parts can pivot without extra nodes.
internal readonly struct RavenPen
{
    private readonly IRavenPainter _target;
    private readonly Transform2D _transform;

    internal RavenPen(IRavenPainter target) : this(target, Transform2D.Identity) { }
    private RavenPen(IRavenPainter target, Transform2D transform) { _target = target; _transform = transform; }

    internal RavenPen With(Transform2D local) => new(_target, _transform * local);
    internal RavenPen At(Vector2 pivot, float rotation = 0, float scale = 1) => With(new Transform2D(rotation, Vector2.One * scale, 0, pivot));
    internal RavenPen Around(Vector2 pivot, float rotation) =>
        rotation == 0 ? this : With(new Transform2D(rotation, pivot) * new Transform2D(0, -pivot));
    internal Transform2D Transform => _transform;

    internal void Fill(Vector2[] points, Color color) { if (color.A > 0.004f) _target.Fill(Map(points), color); }
    internal void Shade(Vector2[] points, Color[] colors) => _target.Shade(Map(points), colors);
    internal void Stroke(Vector2[] points, Color color, float width, bool closed = false)
    {
        if (color.A > 0.004f) _target.Stroke(Map(points), color, width * Mathf.Sqrt(Mathf.Abs(_transform.Determinant())), closed);
    }

    internal void Ink(Vector2[] points, Color fill, float outline = 3.5f)
    {
        Fill(points, fill);
        Stroke(points, RavenStyleHooks.Ink, outline, true);
    }

    // Linear two-colour ramp along from->to, evaluated per vertex (exact under Gouraud shading).
    internal void Ramp(Vector2[] points, Color a, Color b, Vector2 from, Vector2 to)
    {
        var axis = to - from;
        float length = Math.Max(axis.LengthSquared(), 1e-4f);
        var colors = new Color[points.Length];
        for (int i = 0; i < points.Length; i++) colors[i] = a.Lerp(b, Mathf.Clamp((points[i] - from).Dot(axis) / length, 0, 1));
        Shade(points, colors);
    }

    // Soft radial falloff drawn as a triangle fan; no blend-mode change inside one canvas item.
    internal void Glow(Vector2 center, Vector2 radius, Color color, int segments = 28)
    {
        if (color.A <= 0.004f) return;
        var mid = new Color(color, color.A * 0.38f);
        var clear = new Color(color, 0);
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.Tau * i / segments, a1 = Mathf.Tau * (i + 1) / segments;
            Vector2 d0 = new(Mathf.Cos(a0) * radius.X, Mathf.Sin(a0) * radius.Y), d1 = new(Mathf.Cos(a1) * radius.X, Mathf.Sin(a1) * radius.Y);
            Shade([center, center + d0 * 0.42f, center + d1 * 0.42f], [color, mid, mid]);
            Shade([center + d0 * 0.42f, center + d0, center + d1, center + d1 * 0.42f], [mid, clear, clear, mid]);
        }
    }

    private Vector2[] Map(Vector2[] points)
    {
        if (_transform == Transform2D.Identity) return points;
        var mapped = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++) mapped[i] = _transform * points[i];
        return mapped;
    }

    internal static bool Drawable(Vector2[] points)
    {
        if (points.Length < 3) return false;
        float area = 0;
        for (int i = 0; i < points.Length; i++) area += points[i].Cross(points[(i + 1) % points.Length]);
        return Mathf.Abs(area) > 1f;
    }

    // Smooth outline through control points (centripetal Catmull-Rom), for organic shapes.
    internal static Vector2[] Curve(Vector2[] controls, int steps = 6, bool closed = true)
    {
        int n = controls.Length, segments = closed ? n : n - 1;
        List<Vector2> points = new(segments * steps + 1);
        for (int s = 0; s < segments; s++)
        {
            Vector2 p0 = controls[Index(s - 1)], p1 = controls[Index(s)], p2 = controls[Index(s + 1)], p3 = controls[Index(s + 2)];
            float t1 = Knot(p0, p1), t2 = t1 + Knot(p1, p2), t3 = t2 + Knot(p2, p3);
            for (int k = 0; k < steps; k++)
            {
                float t = Mathf.Lerp(t1, t2, k / (float)steps);
                Vector2 a1 = Mix(p0, p1, 0, t1, t), a2 = Mix(p1, p2, t1, t2, t), a3 = Mix(p2, p3, t2, t3, t);
                points.Add(Mix(Mix(a1, a2, 0, t2, t), Mix(a2, a3, t1, t3, t), t1, t2, t));
            }
        }
        if (!closed) points.Add(controls[^1]);
        return [.. points];

        int Index(int i) => closed ? (i % n + n) % n : Math.Clamp(i, 0, n - 1);
        static float Knot(Vector2 a, Vector2 b) => Math.Max(Mathf.Sqrt(a.DistanceTo(b)), 1e-3f);
        static Vector2 Mix(Vector2 a, Vector2 b, float ta, float tb, float t) => tb - ta < 1e-6f ? a : a.Lerp(b, (t - ta) / (tb - ta));
    }
}

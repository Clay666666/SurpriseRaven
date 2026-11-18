using Godot;

namespace SurpriseRaven.Hooks.Raven;

// Engine-free palette and geometry: linked by the game and by the offline preview renderer.
internal static partial class RavenStyleHooks
{
    internal static readonly Color Cream = new(1f, 0.965f, 0.886f);
    internal static readonly Color TextOutline = new(0.29f, 0.235f, 0.165f, 0.85f);
    internal static readonly Color Ink = new(0.07f, 0.055f, 0.045f);
    internal static readonly Color Gold = new(0.94f, 0.78f, 0.38f);
    internal static readonly Color PanelFill = new(0.07f, 0.11f, 0.13f, 0.93f);
    internal static readonly Color Warm = new(1f, 0.66f, 0.3f);
    internal static readonly Color Moon = new(0.55f, 0.64f, 0.86f);
    internal static readonly Vector2 Design = new(1920, 1080);

    internal static Vector2[] Ellipse(Vector2 center, float rx, float ry, int segments = 48, float from = 0, float to = Mathf.Tau)
    {
        var points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float a = from + (to - from) * i / segments;
            points[i] = center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        return points;
    }
}

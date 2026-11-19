using Godot;
using static SurpriseRaven.Hooks.Raven.RavenStyleHooks;

namespace SurpriseRaven.Hooks.Raven;

internal static class RavenBirdEyeHooks
{
    internal static readonly Vector2 Center = new(-63, -53);
    private static readonly Vector2[] Aperture = RavenPen.Curve(
        [new(-68, -53), new(-65, -56), new(-59, -55), new(-57.5f, -54), new(-61, -50), new(-65, -50)], 3);
    private static readonly Vector2[] Pupil = Ellipse(Center, 2.1f, 2.7f, 12)[..^1];
    private static readonly Vector2[] Glint = Ellipse(Center + new Vector2(-1, -1.3f), 1.1f, 1.1f, 8)[..^1];
    private static readonly (Vector2 Inward, float Clearance)[] Limits = EyeLimits();
    private const float PointerGain = 0.0125f;

    internal static Vector2 Offset(Transform2D headToViewport, Vector2? pointer)
    {
        if (pointer is not { } position || !position.IsFinite() || !headToViewport.IsFinite()
            || Mathf.IsZeroApprox(headToViewport.Determinant()))
            return Vector2.Zero;

        var fromCenter = position - headToViewport * Center;
        var offset = headToViewport.AffineInverse().BasisXform(fromCenter) * PointerGain;
        if (!offset.IsFinite()) return Vector2.Zero;

        float fraction = 1f;
        foreach (var (inward, clearance) in Limits)
        {
            float towardEdge = -inward.Dot(offset);
            if (towardEdge > clearance) fraction = Math.Min(fraction, clearance / towardEdge);
        }
        return offset * fraction;
    }

    internal static void Paint(RavenPen pen, float blink, Vector2 offset)
    {
        var lid = pen.With(new Transform2D(0, new Vector2(1, 1 - 0.88f * blink), 0, Center) * new Transform2D(0, -Center));
        lid.Fill(Aperture, new Color(0.98f, 0.64f, 0.22f));
        var gaze = lid.At(offset);
        gaze.Fill(Pupil, Ink);
        if (blink < 0.5f) gaze.Fill(Glint, Cream);
    }

    private static (Vector2 Inward, float Clearance)[] EyeLimits()
    {
        var marks = Pupil.Concat(Glint).ToArray();
        var limits = new (Vector2, float)[Aperture.Length];
        for (int index = 0; index < Aperture.Length; index++)
        {
            var start = Aperture[index];
            var edge = Aperture[(index + 1) % Aperture.Length] - start;
            var inward = new Vector2(-edge.Y, edge.X).Normalized();
            if (inward.Dot(Center - start) < 0) inward = -inward;
            float clearance = marks.Min(point => inward.Dot(point - start));
            limits[index] = (inward, Math.Max(0, clearance - 0.05f));
        }
        return limits;
    }
}

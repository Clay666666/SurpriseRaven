using Godot;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestRavenGaze()
    {
        var center = RavenBirdEyeHooks.Center;
        var identity = Transform2D.Identity;
        Check(RavenBirdEyeHooks.Offset(identity, center) == Vector2.Zero, "A centered pointer shifts the pupil.");
        Check(RavenBirdEyeHooks.Offset(identity, null) == Vector2.Zero, "An absent pointer shifts the pupil.");
        Check(RavenBirdEyeHooks.Offset(identity, new Vector2(float.NaN, 0)) == Vector2.Zero, "Invalid pointer coordinates shift the pupil.");
        Check(RavenBirdEyeHooks.Offset(new Transform2D(Vector2.Zero, Vector2.Down, Vector2.Zero), center) == Vector2.Zero,
            "An edge-on singular transform shifts the pupil.");

        foreach (var direction in new[] { Vector2.Left, Vector2.Right, Vector2.Up, Vector2.Down })
        {
            var near = RavenBirdEyeHooks.Offset(identity, center + direction * 10);
            var far = RavenBirdEyeHooks.Offset(identity, center + direction * 4000);
            var farther = RavenBirdEyeHooks.Offset(identity, center + direction * 8000);
            Check(near.Dot(direction) > 0 && far.Dot(direction) > near.Dot(direction), "The pupil does not follow a cardinal direction.");
            Check(far.IsEqualApprox(farther) && far.Length() < 5, "Far pointers exceed the pupil's travel bounds.");
        }

        var page = new Transform2D(0, new Vector2(0.85f, 0.68f), 0, new Vector2(32, 108));
        foreach (float facing in new[] { -1f, -0.02f, 1f })
        {
            var bird = new Transform2D(0.32f, new Vector2(facing * 0.6f, 0.6f), 0, new Vector2(1324, 298));
            var head = page * bird * RavenBirdArtHooks.HeadTransform(-0.22f);
            var origin = head * center;
            var direction = new Vector2(350, 220);
            var offset = RavenBirdEyeHooks.Offset(head, origin + direction);
            var displayed = head.BasisXform(offset);
            Check(!displayed.IsZeroApprox() && displayed.Normalized().Dot(direction.Normalized()) > 0.999f,
                "The turned or flying raven looks away after the page transform.");
            Check(RavenBirdEyeHooks.Offset(head, origin).IsZeroApprox(), "An eye-centered pointer drifts after transforms.");
        }

        var painter = new EyeFixturePainter();
        for (int bearing = 0; bearing < 32; bearing++)
        {
            var direction = Vector2.FromAngle(bearing * Mathf.Tau / 32f);
            var offset = RavenBirdEyeHooks.Offset(identity, center + direction * 4000);
            foreach (float blink in new[] { 0f, 0.45f, 1f })
            {
                painter.Fills.Clear();
                RavenBirdEyeHooks.Paint(new RavenPen(painter), blink, offset);
                Check(painter.Fills.Count == (blink < 0.5f ? 3 : 2), "A blink changed the expected eye layers.");
                var aperture = painter.Fills[0];
                Check(painter.Fills.Skip(1).SelectMany(points => points).All(point => Geometry2D.IsPointInPolygon(point, aperture)),
                    $"The pupil or glint escaped the eyelid at bearing {bearing} and blink {blink}.");
            }
        }
    }

    private sealed class EyeFixturePainter : IRavenPainter
    {
        internal List<Vector2[]> Fills { get; } = [];
        public void Fill(Vector2[] points, Color color) => Fills.Add(points);
        public void Shade(Vector2[] points, Color[] colors) => throw new NotSupportedException();
        public void Stroke(Vector2[] points, Color color, float width, bool closed) => throw new NotSupportedException();
    }
}

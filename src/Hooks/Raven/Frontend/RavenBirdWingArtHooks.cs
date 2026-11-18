using Godot;
using static SurpriseRaven.Hooks.Raven.RavenStyleHooks;

namespace SurpriseRaven.Hooks.Raven;

// The two wing sections are painted in span/chord coordinates before the rig projects each wing.
internal static class RavenBirdWingArtHooks
{
    private const float Wrist = 58;
    private static readonly Vector2 NearShoulder = new(-12, -20), FarShoulder = new(-2, -25);
    private static readonly Vector2 Up = new(0.12f, -1f), NearDepth = new(-0.24f, 0.5f), FarDepth = new(0.2f, -0.42f);
    private static readonly Vector2[] Arm =
    [
        .. RavenPen.Curve([new(-5, -5), new(12, -10), new(32, -12), new(Wrist + 4, -7)], 4, false),
        new(64, 16), new(55, 14), new(59, 21), new(46, 19), new(49, 26), new(34, 23), new(36, 30),
        new(23, 27), new(22, 34), new(12, 30), new(9, 36), new(1, 30), new(-4, 24)
    ];
    private static readonly Vector2[] ArmCoverts =
    [
        new(-2, -3), new(14, -8), new(34, -10), new(59, -6), new(58, 5), new(49, 2), new(53, 10),
        new(41, 6), new(43, 14), new(31, 9), new(31, 18), new(20, 12), new(17, 19), new(8, 13), new(3, 16)
    ];
    private static readonly Vector2[] HandCoverts =
    [
        .. RavenPen.Curve([new(-7, -4), new(8, -9), new(29, -8), new(43, -4)], 4, false),
        new(53, 0), new(41, -1), new(46, 4), new(33, 2), new(37, 8), new(24, 5), new(27, 11),
        new(14, 8), new(14, 14), new(-2, 10)
    ];
    private static readonly float[] Angles = [-0.04f, 0.12f, 0.28f, 0.45f, 0.63f], Lengths = [76, 74, 68, 59, 49];

    internal static float Elevation(float lift) => Mathf.Lerp(-0.95f, 1.2f, (lift + 1) / 2);

    internal static void Paint(RavenPen pen, in RavenBirdPose pose, bool far)
    {
        float elevation = Elevation(pose.Lift), perspective = far ? 0.88f : 1f;
        var span = ((far ? FarDepth : NearDepth) * Mathf.Cos(elevation) + Up * Mathf.Sin(elevation)) * perspective;
        var chord = new Vector2(1f, 0.1f) * perspective;
        var wing = pen.With(new Transform2D(span, chord, far ? FarShoulder : NearShoulder));
        float fold = pose.Fold, armLength = 1 - 0.2f * fold;
        float tone = far ? 0.38f : 0;
        Color baseColor = RavenBirdArtHooks.Plumage.Darkened(tone), shadow = RavenBirdArtHooks.Shadow.Darkened(tone);
        Color indigo = RavenBirdArtHooks.Indigo.Darkened(tone), sheen = RavenBirdArtHooks.Sheen.Darkened(tone);
        var hand = wing.With(new Transform2D(fold * 2.2f, new Vector2(Wrist * armLength, 0)));
        for (int i = Angles.Length - 1; i >= 0; i--)
        {
            float angle = Angles[i] * (1 - 0.5f * fold), length = Lengths[i] * (1 - 0.12f * fold);
            var root = new Vector2(3 * i, 2 + 3 * i);
            var tip = root + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length;
            var feather = RavenBirdArtHooks.Feather(root, tip, 6.5f);
            RavenBirdArtHooks.PaintFeather(hand, feather, baseColor, indigo);
        }
        var arm = wing.With(new Transform2D(0, new Vector2(armLength, 1), 0, Vector2.Zero));
        arm.Ramp(Arm, indigo, shadow, new Vector2(20, -9), new Vector2(27, 28));
        arm.Fill(ArmCoverts, indigo);
        arm.Fill([new(0, -3), new(16, -7), new(37, -9), new(55, -6), new(37, -5),
            new(42, -1), new(26, -4), new(31, 0), new(15, -2), new(9, 2)], sheen);
        arm.Stroke(Arm, Ink, 2.5f, true);
        hand.Ink(HandCoverts, indigo, 1.9f);
        hand.Fill([new(-1, -4), new(12, -7), new(29, -6), new(42, -3),
            new(27, -4), new(31, -1), new(16, -3), new(21, 1), new(6, -1)], sheen);
    }
}

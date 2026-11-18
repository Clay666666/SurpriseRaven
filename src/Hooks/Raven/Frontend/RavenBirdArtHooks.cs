using Godot;
using static SurpriseRaven.Hooks.Raven.RavenStyleHooks;

namespace SurpriseRaven.Hooks.Raven;

// All parts are painted around the pivots of the existing side-view rig.
internal static class RavenBirdArtHooks
{
    internal static readonly Vector2 BeakTip = new(-106, -40), CarryPoint = new(-97, -31), Feet = new(-8, 46);
    internal static readonly Color Plumage = new(0.105f, 0.112f, 0.185f), Shadow = new(0.055f, 0.051f, 0.09f);
    internal static readonly Color Indigo = new(0.215f, 0.244f, 0.365f), Sheen = new(0.37f, 0.47f, 0.57f);
    private static readonly Color CandleRim = new(0.57f, 0.37f, 0.26f), Foot = new(0.33f, 0.29f, 0.32f);
    private static readonly Vector2 Neck = new(-34, -30), Shoulder = new(-30, -18), TailRoot = new(38, 2);
    private static readonly Vector2[] Torso =
    [
        .. RavenPen.Curve([new(-36, -38), new(-7, -36), new(21, -24), new(50, -6)], 5, false),
        new(59, 3), new(47, 3), new(53, 8), new(35, 16), new(37, 19),
        .. RavenPen.Curve([new(28, 20), new(6, 25), new(-11, 28), new(-31, 22)], 4, false),
        new(-27, 26), new(-41, 18), new(-46, 7), new(-47, 13), new(-52, -4), new(-50, -22)
    ];
    private static readonly Vector2[] Tail =
    [
        .. RavenPen.Curve([new(32, -9), new(63, -2), new(93, 7), new(108, 11)], 4, false),
        new(102, 13), new(115, 17), new(111, 20), new(97, 20), new(101, 23),
        new(82, 23), new(61, 18), new(64, 21), new(41, 15), new(30, 9)
    ];
    private static readonly Vector2[] Head =
    [
        .. RavenPen.Curve([new(-71, -39), new(-76, -52), new(-67, -63), new(-57, -68), new(-41, -67), new(-31, -61)], 4, false),
        new(-19, -56), new(-27, -55), new(-14, -44), new(-23, -46), new(-13, -31), new(-23, -35),
        new(-18, -23), new(-29, -27), new(-27, -13), new(-37, -21), new(-38, -7), new(-45, -16),
        new(-49, -3), new(-54, -15), new(-59, -10), new(-60, -24), new(-64, -20), new(-65, -32)
    ];
    private static readonly Vector2[] Bill =
    [
        .. RavenPen.Curve([new(-67, -55), new(-82, -55), new(-95, -51), new(-103, -45), BeakTip], 4, false),
        new(-99, -42), new(-91, -42), new(-81, -40), new(-70, -40), new(-65, -45)
    ];
    private static readonly Vector2[] Mandible =
        [new(-69, -42), new(-82, -41), new(-95, -42), new(-101, -41), new(-93, -36), new(-84, -33), new(-72, -33), new(-68, -36)];
    private static readonly Vector2[] Mantle =
    [
        .. RavenPen.Curve([new(-34, -16), new(-28, -29), new(-12, -37), new(8, -35), new(34, -24)], 5, false),
        new(56, -12), new(43, -14), new(58, -3), new(42, -6), new(47, 0), new(31, -2),
        new(31, 4), new(18, 2), new(15, 8), new(2, 6), new(-4, 11), new(-18, 8), new(-30, 1)
    ];
    private static readonly Vector2[][] Primaries =
    [
        Feather(new(-2, 1), new(67, 23), 7), Feather(new(2, -5), new(83, 19), 7),
        Feather(new(6, -11), new(95, 13), 7), Feather(new(12, -17), new(103, 6), 6.5f)
    ];
    private static readonly Vector2 Eye = new(-63, -53);
    private static readonly Vector2[] EyeShape = RavenPen.Curve(
        [new(-68, -53), new(-65, -56), new(-59, -55), new(-57.5f, -54), new(-61, -50), new(-65, -50)], 3);

    internal static void Paint(RavenPen pen, in RavenBirdPose pose)
    {
        bool spread = pose.Flight >= 0.35f;
        if (spread) RavenBirdWingArtHooks.Paint(pen, pose, far: true);
        Legs(pen.With(new Transform2D(0, new Vector2(9, -1))), pose, Foot.Darkened(0.4f));
        PaintTail(pen.Around(TailRoot, pose.Tail));
        pen.Ramp(Torso, Indigo, Shadow, new Vector2(-5, -32), new Vector2(-18, 26));
        pen.Fill([new(-45, -11), new(-32, 1), new(-36, -8), new(-19, 5), new(-9, 8), new(-17, 11),
            new(4, 16), new(-4, 19), new(15, 19), new(-10, 24), new(-31, 18), new(-41, 8)], Plumage);
        pen.Fill([new(-43, 3), new(-34, 15), new(-22, 20), new(-28, 19), new(-16, 24), new(-33, 19)], CandleRim);
        pen.Stroke(Torso, Ink, 3, true);
        Legs(pen, pose, Foot);
        if (!spread) PaintFoldedWing(pen.Around(Shoulder, -0.12f * pose.Ruffle - 0.3f * pose.Flight));
        PaintHead(pen.Around(Neck, pose.Head), pose);
        if (spread) RavenBirdWingArtHooks.Paint(pen, pose, far: false);
    }

    private static void PaintTail(RavenPen pen)
    {
        pen.Ink(Tail, Shadow, 2.8f);
        pen.Fill([new(37, -6), new(64, -1), new(102, 10), new(88, 9), new(109, 16), new(73, 10)], Indigo);
        pen.Fill([new(40, 1), new(81, 13), new(101, 18), new(76, 14)], Plumage.Lerp(Sheen, 0.48f));
        pen.Stroke([new(56, 14), new(82, 20), new(93, 20.5f)], CandleRim.Darkened(0.38f), 1.3f);
    }

    private static void PaintFoldedWing(RavenPen pen)
    {
        foreach (var feather in Primaries) PaintFeather(pen, feather, Plumage, Indigo);
        pen.Ramp(Mantle, Indigo, Plumage, new Vector2(-9, -34), new Vector2(6, 7));
        pen.Fill([new(-28, -20), new(-14, -33), new(3, -32), new(24, -24), new(12, -25),
            new(34, -14), new(11, -20), new(16, -15), new(-3, -22), new(-15, -24)], Sheen);
        pen.Fill([new(-21, -14), new(-7, -20), new(4, -15), new(19, -10), new(10, -10), new(21, -4),
            new(5, -8), new(8, -4), new(-6, -10)], Indigo.Lerp(Sheen, 0.47f));
        pen.Fill([new(-28, -9), new(-16, -4), new(-5, -1), new(-13, 0), new(-2, 5), new(-17, 2)], Indigo);
        pen.Stroke(Mantle, Ink, 2.6f, true);
    }

    private static void PaintHead(RavenPen pen, in RavenBirdPose pose)
    {
        pen.Ramp(Head, Indigo, Shadow, new Vector2(-49, -65), new Vector2(-51, -17));
        pen.Fill([new(-72, -55), new(-64, -62), new(-54, -65), new(-41, -64), new(-32, -59),
            new(-42, -60), new(-34, -55), new(-51, -59), new(-65, -58)], Sheen);
        pen.Fill([new(-51, -53), new(-40, -51), new(-27, -40), new(-38, -44), new(-29, -33),
            new(-42, -40), new(-38, -31), new(-46, -40)], Indigo);
        pen.Fill([new(-64, -39), new(-55, -42), new(-46, -46), new(-51, -37), new(-45, -38),
            new(-51, -28), new(-49, -21), new(-55, -26), new(-57, -20), new(-60, -30)], Plumage.Lerp(Sheen, 0.43f));
        pen.Fill([new(-59, -29), new(-55, -26), new(-53, -19), new(-57, -21)], CandleRim);
        pen.Stroke(Head, Ink, 2.8f, true);

        var jaw = pen.Around(new Vector2(-71, -40.4f), -0.42f * pose.Beak);
        jaw.Ink(Mandible, new Color(0.27f, 0.24f, 0.26f), 2.3f);
        jaw.Fill([new(-98, -40), new(-84, -37), new(-73, -37), new(-83, -35)], CandleRim);
        pen.Ink(Bill, new Color(0.19f, 0.20f, 0.26f), 2.9f);
        pen.Fill([new(-102, -44), new(-94, -50), new(-83, -53), new(-72, -53), new(-77, -49), new(-89, -49)], Sheen);
        pen.Fill([new(-80, -48), new(-76, -50), new(-72, -50), new(-75, -47)], Ink);
        pen.Fill([new(-72, -55), new(-59, -59), new(-50, -55), new(-57, -50), new(-67, -48),
            new(-76, -45), new(-71, -50), new(-77, -49)], Shadow);
        var lid = pen.With(new Transform2D(0, new Vector2(1, 1 - 0.88f * pose.Blink), 0, Eye) * new Transform2D(0, -Eye));
        lid.Fill(EyeShape, new Color(0.98f, 0.64f, 0.22f));
        lid.Fill(Ellipse(Eye, 2.1f, 2.7f, 12)[..^1], Ink);
        if (pose.Blink < 0.5f) lid.Fill(Ellipse(Eye + new Vector2(-1, -1.3f), 1.1f, 1.1f, 8)[..^1], Cream);
    }

    private static readonly (Vector2 Tucked, Vector2 Standing)[] Toes =
        [(new(29, 26), new(-25, 44.5f)), (new(31, 23), new(-19, 46.5f)), (new(28, 20), new(-11, 46)), (new(20, 19), new(5, 44.5f))];

    private static void Legs(RavenPen pen, in RavenBirdPose pose, Color color)
    {
        float stand = pose.Flight < 0.35f ? 1 : pose.Legs;
        Vector2 hip = new Vector2(4, 20).Lerp(new(-4, 20), stand), ankle = new Vector2(24, 22).Lerp(new(-7, 41), stand);
        pen.Stroke([hip, ankle], Ink, 4.8f);
        pen.Stroke([hip + new Vector2(-0.7f, 0), ankle], color, 2.9f);
        foreach (var (tucked, standing) in Toes)
        {
            var tip = tucked.Lerp(standing, stand);
            var knuckle = ankle.Lerp(tip, 0.55f) + new Vector2(0, stand * 1.6f);
            pen.Stroke([ankle, knuckle, tip], Ink, 3.3f);
            pen.Stroke([ankle, knuckle, tip], color, 2);
        }
    }

    // Primary feathers have a curved outer vane and a narrower inner vane separated by a tapered shaft.
    internal static Vector2[] Feather(Vector2 root, Vector2 tip, float width)
    {
        var axis = tip - root;
        var across = new Vector2(-axis.Y, axis.X).Normalized() * width;
        return [root + across, root + axis * 0.46f + across * 1.22f, root + axis * 0.79f + across * 0.92f,
            root + axis * 0.76f + across * 0.62f, tip, root + axis * 0.66f - across * 0.73f, root - across * 0.6f];
    }

    internal static void PaintFeather(RavenPen pen, Vector2[] feather, Color baseColor, Color litVane)
    {
        pen.Ink(feather, baseColor, 1.8f);
        pen.Fill([feather[6].Lerp(feather[0], 0.28f), feather[5], feather[5].Lerp(feather[4], 0.8f)], litVane);
    }
}

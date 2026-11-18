using Godot;
using static SurpriseRaven.Hooks.Raven.RavenStyleHooks;

namespace SurpriseRaven.Hooks.Raven;

// Inked wrought iron and folded copper, on the original stone platform. Both renderers use this geometry.
internal static class RavenLampPaintHooks
{
    internal const float GlassTop = 342, GlassBottom = 436;
    private static readonly Color Iron = new(0.19f, 0.22f, 0.27f), Edge = new(0.41f, 0.47f, 0.51f), Deep = new(0.09f, 0.1f, 0.14f);
    private static readonly Color Copper = new(0.6f, 0.37f, 0.21f), Worn = new(0.88f, 0.67f, 0.43f), Patina = new(0.27f, 0.39f, 0.38f);
    private static readonly Color StoneFace = new(0.25f, 0.255f, 0.29f), StoneDark = new(0.12f, 0.125f, 0.15f);
    private static readonly Color StoneTop = new(0.33f, 0.33f, 0.36f), StoneLit = new(0.5f, 0.41f, 0.32f);
    private static readonly Color Glass = new(0.14f, 0.19f, 0.23f), Joint = new(0, 0, 0, 0.4f);
    private static readonly (float Half, float Top, float Bottom)[] Steps = [(250, 828, 870), (190, 790, 828), (132, 754, 790)];
    // All pane shapes are relative to the apex. The central lancet leaves the wick and flame visible.
    private static readonly Vector2[] Lancet = [new(0, 2), new(15, 15), new(23, 30), new(16, 82), new(7, 93), new(-7, 93), new(-16, 82), new(-23, 30), new(-15, 15)];
    private static readonly Vector2[] Cage = [new(0, -1), new(38, 14), new(30, 80), new(16, 90), new(8, 94), new(-8, 94), new(-16, 90), new(-30, 80), new(-38, 14)];
    private static readonly Vector2[] Bow = [
        .. RavenPen.Curve([new(3, 319), new(38, 313), new(93, 291), new(128, 282), new(151, 286), new(164, 302)], 4, false),
        new(160, 314), new(153, 300),
        .. RavenPen.Curve([new(143, 295), new(121, 295), new(90, 306), new(47, 328), new(11, 345)], 4, false)];
    private static readonly Vector2[] Brace = [
        .. RavenPen.Curve([new(8, 408), new(25, 371), new(57, 335), new(98, 309), new(134, 294)], 4, false),
        new(139, 294), new(126, 309),
        .. RavenPen.Curve([new(113, 311), new(78, 332), new(45, 365), new(19, 418), new(9, 439)], 4, false)];

    internal static void Paint(RavenPen pen)
    {
        Platform(pen);
        // Offering ring.
        pen.Stroke(Ellipse(RavenLayout.Altar, 128, 30), new Color(Gold, 0.55f), 3);
        for (int i = 0; i < 24; i += 2)
            pen.Stroke(Ellipse(RavenLayout.Altar, 112, 24, 4, i * Mathf.Tau / 24, (i + 1) * Mathf.Tau / 24), new Color(Gold, 0.35f), 2);
        foreach (var (half, top, bottom) in Steps) Step(pen, half, top, bottom);
        var post = pen.At(new Vector2(960, 0));
        Mast(post);
        foreach (int side in new[] { -1, 1 })
        {
            Bracket(post.With(new Transform2D(0, new Vector2(side, 1), 0, Vector2.Zero)), side);
            Lantern(post.At(new Vector2(side * 160, GlassTop)));
        }
        // Flat top at y=276 is the contact edge for both talons.
        post.Ink([new(-20, 276), new(19, 276), new(23, 282), new(16, 289), new(12, 308), new(0, 319), new(-13, 307), new(-17, 288), new(-23, 283)], Copper, 4);
        post.Fill([new(-18, 280), new(16, 280), new(9, 285), new(-16, 285)], Worn);
        post.Fill([new(4, 289), new(15, 287), new(10, 305), new(0, 314)], Deep);
    }

    internal static void LanternLight(RavenPen pen, float x, float strength)
    {
        if (strength <= 0.004f) return;
        var window = pen.At(new Vector2(x, GlassTop));
        window.Ramp(Cage, new Color(0.76f, 0.41f, 0.17f, 0.75f * strength), new Color(0.81f, 0.52f, 0.23f, 0.53f * strength), new(-28, 12), new(25, 80));
        window.Ramp(Lancet, new Color(1f, 0.64f, 0.25f, 0.66f * strength), new Color(1f, 0.86f, 0.53f, 0.82f * strength), new(0, 3), new(0, 64));
        window.Glow(new Vector2(0, 45), new Vector2(20, 28), new Color(1f, 0.85f, 0.5f, 0.46f * strength), 16);
    }

    // Lead lines and the sliver of moonlight sit in front of the wax; neither covers the flame.
    internal static void LanternSheen(RavenPen pen, float x)
    {
        var window = pen.At(new Vector2(x, GlassTop));
        window.Stroke(Cage, Ink, 4, true);
        window.Stroke(Lancet, Ink, 3, true);
        window.Stroke([new(-23, 29), new(-32, 19), new(-37, 11)], Deep, 3);
        window.Stroke([new(23, 29), new(32, 19), new(37, 11)], Deep, 3);
        window.Fill([new(-16, 23), new(-12, 17), new(-16, 43), new(-19, 49)], new Color(Cream, 0.38f));
        window.Stroke([new(-19, 82), new(-8, 89)], Worn, 1.6f);
    }

    private static void Platform(RavenPen pen)
    {
        var rim = Ellipse(new Vector2(960, 905), 720, 128);
        pen.Ramp(rim, new Color(0.23f, 0.24f, 0.28f), new Color(0.08f, 0.09f, 0.12f), new Vector2(0, 777), new Vector2(0, 1033));
        pen.Stroke(rim, Ink, 5);
        pen.Stroke(Ellipse(new Vector2(960, 905), 714, 122, 40, Mathf.Pi + 0.2f, Mathf.Tau - 0.2f), new Color(Moon, 0.16f), 3);
        pen.Fill(Ellipse(new Vector2(960, 896), 690, 112), new Color(0.15f, 0.16f, 0.19f));
        pen.Glow(new Vector2(960, 880), new Vector2(620, 98), new Color(0.29f, 0.3f, 0.34f), 40);
        pen.Glow(new Vector2(960, 862), new Vector2(430, 66), new Color(Warm, 0.1f), 32);
        // Flagstones: a concentric groove and radial seams, each with a faint lit lower edge.
        pen.Stroke(Ellipse(new Vector2(960, 900), 590, 90, 64), new Color(0, 0, 0, 0.2f), 2);
        pen.Stroke(Ellipse(new Vector2(960, 902), 590, 90, 64, 0.15f, Mathf.Pi - 0.15f), new Color(0.45f, 0.46f, 0.52f, 0.1f), 1.5f);
        for (int i = -5; i <= 5; i++)
        {
            var seam = Seam(new Vector2(960 + i * 118, 800), new Vector2(960 + i * 150, 1010));
            pen.Stroke(seam, new Color(0, 0, 0, 0.22f), 2);
            pen.Stroke([seam[0] + new Vector2(2, 0), seam[1] + new Vector2(2, 0)], new Color(0.45f, 0.46f, 0.52f, 0.1f), 1.5f);
        }
        pen.Stroke(Ellipse(new Vector2(960, 896), 690, 112, 64, 0.1f, Mathf.Pi - 0.1f), new Color(Warm, 0.18f), 4);
    }

    private static void Step(RavenPen pen, float half, float top, float bottom)
    {
        var face = Box(half, top, bottom);
        pen.Ramp(face, StoneFace, StoneDark, new Vector2(0, top), new Vector2(0, bottom));
        foreach (float k in new[] { -2f, -1f, 1f, 2f })
        {
            float x = 960 + k * half * 0.42f;
            pen.Stroke([new(x, top + 11), new(x, bottom - 3)], Joint, 2);
            pen.Stroke([new(x + 2, top + 12), new(x + 2, bottom - 4)], new Color(StoneTop, 0.3f), 1);
        }
        // Top face: candlelight pools at both ends, where the candles stand.
        float[] xs = [960 - half + 4, 960 - half * 0.45f, 960, 960 + half * 0.45f, 960 + half - 4];
        Color[] cs = [StoneLit, StoneTop, StoneTop.Darkened(0.08f), StoneTop, StoneLit];
        for (int i = 0; i < 4; i++)
            pen.Shade([new(xs[i], top + 3), new(xs[i + 1], top + 3), new(xs[i + 1], top + 10), new(xs[i], top + 10)], [cs[i], cs[i + 1], cs[i + 1], cs[i]]);
        pen.Stroke([new(960 - half + 4, top + 10.5f), new(960 + half - 4, top + 10.5f)], Joint, 1.5f);
        pen.Stroke(face, Ink, 4, true);
    }

    private static void Mast(RavenPen p)
    {
        p.Ink([new(-14, 288), new(14, 288), new(11, 419), new(10, 530), new(17, 682), new(28, 713),
            new(-28, 713), new(-18, 681), new(-11, 530), new(-12, 419)], Iron, 4);
        p.Fill([new(1, 305), new(12, 305), new(8, 528), new(15, 680), new(6, 690), new(-1, 528)], Deep);
        p.Fill([new(-10, 427), new(-5, 418), new(-4, 491), new(-8, 528)], Edge);
        p.Fill([new(-8, 551), new(-6, 545), new(-6, 633), new(-13, 675)], Patina);
        Collar(p, 417, 18);
        Collar(p, 684, 24);
        // Two flared buttresses meet the upper stair without changing its footprint.
        p.Ink([new(-52, 752), new(-50, 739), new(-32, 726), new(-21, 692), new(20, 692), new(32, 726), new(51, 740), new(52, 752)], Iron, 4);
        p.Fill([new(-46, 739), new(-29, 725), new(-19, 696), new(-11, 698), new(-20, 729)], Edge);
        p.Fill([new(4, 697), new(19, 697), new(28, 727), new(46, 741), new(12, 733)], Deep);
        p.Ink([new(-52, 743), new(-16, 734), new(19, 734), new(52, 743), new(52, 752), new(-52, 752)], Copper, 3);
        p.Fill([new(-45, 744), new(-14, 737), new(15, 737), new(6, 740), new(-21, 743)], Worn);
    }

    private static void Collar(RavenPen p, float y, float half)
    {
        var band = p.At(new Vector2(0, y));
        band.Ink([new(-half, -8), new(-6, -11), new(half, -7), new(half - 3, 3), new(0, 12), new(2 - half, 3)], Copper, 3);
        band.Fill([new(0, 6), new(2, -4), new(half - 3, -5), new(half - 5, 1)], Deep);
        band.Stroke([new(3 - half, -6), new(-6, -8), new(3, -7)], Worn, 2);
    }

    private static void Bracket(RavenPen p, int side)
    {
        p.Ink(Brace, Deep, 4);
        p.Stroke(RavenPen.Curve([new(14, 404), new(33, 365), new(63, 334), new(97, 313)], 4, false), Patina, 2.8f);
        p.Ink(Bow, Iron, 4);
        p.Stroke(RavenPen.Curve([new(17, 320), new(45, 313), new(94, 294), new(129, 285), new(147, 287)], 4, false), side < 0 ? Edge : Patina, 3);
        // A copper ferrule binds the lower brace into the bow.
        p.Ink([new(100, 291), new(107, 289), new(114, 307), new(107, 310)], Copper, 2);
    }

    private static void Lantern(RavenPen p)
    {
        p.Stroke([new(0, -40), new(-6, -33), new(-5, -25), new(0, -22), new(5, -28), new(4, -34)], Ink, 5, true);
        p.Stroke([new(-4, -32), new(-4, -27), new(-1, -25)], Worn, 2);
        p.Ink(Cage, Glass, 4);
        p.Fill(Lancet, new Color(0.18f, 0.24f, 0.28f));
        // The folded hood overhangs the glass on both sides; a single copper seam catches the moon.
        p.Ink([new(-44, 10), new(-30, -2), new(-11, -17), new(-5, -25), new(4, -25), new(12, -16), new(31, 0), new(44, 10), new(33, 16), new(0, -2), new(-34, 16)], Iron, 4);
        p.Fill([new(2, -21), new(10, -14), new(36, 7), new(32, 10), new(3, -4)], Deep);
        p.Stroke([new(-36, 7), new(-16, -6), new(-5, -18)], Copper, 3);
        p.Ink([new(-31, 79), new(-17, 89), new(-8, 94), new(8, 94), new(17, 89), new(31, 79), new(26, 96), new(8, 107), new(0, 122), new(-8, 107), new(-26, 96)], Iron, 4);
        p.Fill([new(-26, 86), new(-14, 94), new(-4, 97), new(7, 97), new(23, 89), new(19, 95), new(0, 103), new(-23, 94)], Copper);
        p.Fill([new(-6, 106), new(1, 104), new(-1, 115)], Patina);
    }

    // Clips a seam to the platform top (inner ellipse) so it never runs onto the rim or the sky.
    private static Vector2[] Seam(Vector2 a, Vector2 b)
    {
        Vector2 k = new(1 / 682f, 1 / 106f), p = (a - new Vector2(960, 896)) * k, d = (b - a) * k;
        float qa = d.Dot(d), qb = 2 * p.Dot(d), disc = Mathf.Sqrt(Mathf.Max(0, qb * qb - 4 * qa * (p.Dot(p) - 1)));
        return [a.Lerp(b, Mathf.Max(0, (-qb - disc) / (2 * qa))), a.Lerp(b, Mathf.Min(1, (-qb + disc) / (2 * qa)))];
    }

    private static Vector2[] Box(float half, float top, float bottom) =>
        [new(960 - half, top), new(960 + half, top), new(960 + half, bottom), new(960 - half, bottom)];
}

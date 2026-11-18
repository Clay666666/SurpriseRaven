using Godot;
using static SurpriseRaven.Hooks.Raven.RavenStyleHooks;

namespace SurpriseRaven.Hooks.Raven;

// Node-local candle layout: node rect, wick tip (flame base), additive halo, flame and smoke sheets.
internal readonly record struct RavenCandleFrame(Vector2 Size, Vector2 Position, Vector2 Wick, Rect2 Glow, Rect2 Flame, Rect2 Smoke)
{
    internal Vector2 FlamePivot => new(Flame.Size.X / 2, Flame.Size.Y * 0.9f);
    internal Vector2 SmokePivot => new(Smoke.Size.X / 2, Smoke.Size.Y * 0.9f);
}

// The lit side faces the centre of the altar. The same shapes and light strength feed the offline preview.
internal static class RavenCandleArtHooks
{
    internal const float StandHeight = 120, FlameFps = 24;
    private static readonly Color Char = new(0.1f, 0.075f, 0.06f), Ember = new(1f, 0.46f, 0.16f);
    private static readonly Color Shadow = new(0.01f, 0.01f, 0.03f, 0.55f);
    private static readonly Color ColdFace = new(0.58f, 0.59f, 0.64f), Bone = new(0.91f, 0.81f, 0.63f);
    private static readonly Color ColdShade = new(0.36f, 0.37f, 0.43f), Honey = new(0.62f, 0.43f, 0.30f);
    private static readonly Color ColdLip = new(0.75f, 0.76f, 0.80f), Ivory = new(1f, 0.93f, 0.76f);
    private static readonly Color WornEdge = new(0.77f, 0.68f, 0.49f), Gilt = new(1f, 0.82f, 0.47f);
    private static readonly Color Patina = new(0.29f, 0.33f, 0.32f), Bronze = new(0.56f, 0.37f, 0.21f);

    private static readonly Vector2[] Foot = [new(-36, -7), new(-31, -13), new(-16, -17), new(-7, -29),
        new(7, -29), new(15, -18), new(30, -14), new(37, -8), new(32, -3), new(7, 0), new(-22, -2)];
    private static readonly Vector2[] StemRib = [new(-4, -101), new(-5, -88), new(-16, -72), new(-17, -66),
        new(-7, -51), new(-6, -27), new(1, -27), new(1, -52), new(-10, -68), new(2, -87), new(3, -101)];
    private static readonly Vector2[] Chalice = [new(-29, -121), new(-19, -115), new(-11, -115), new(0, -120),
        new(12, -115), new(21, -116), new(29, -122), new(24, -109), new(8, -100), new(6, -96),
        new(-5, -96), new(-9, -101), new(-24, -108)];

    internal static RavenCandleFrame Frame(RavenCandleSpot spot)
    {
        float stand = spot.Kind == RavenCandleKind.Brass ? StandHeight : 0, flame = spot.Width * 2.8f;
        var size = new Vector2(Mathf.Max(spot.Width * 6, 160), spot.Height + stand + 280);
        Vector2 wick = new(size.X / 2, size.Y - stand - spot.Height - 4), smoke = new(flame * 1.8f, flame * 3.6f);
        return new RavenCandleFrame(size, spot.Base - new Vector2(size.X / 2, size.Y), wick,
            new Rect2(wick - new Vector2(95, 110), new Vector2(190, 190)),
            new Rect2(wick - new Vector2(flame / 2, flame * 0.88f), new Vector2(flame, flame)),
            new Rect2(wick - new Vector2(smoke.X / 2, smoke.Y * 0.95f), smoke));
    }

    // Restrained halo flicker from two incommensurate sines; the flame core breathes with the same value.
    internal static float Flicker(double time, int index) =>
        0.94f + 0.035f * Mathf.Sin((float)time * 7.3f + index) + 0.025f * Mathf.Sin((float)time * 12.9f + 2.1f * index);
    internal static double FlameFrame(double time, int index) => time * FlameFps + index * 7;
    internal static Color GlowColor(float strength, float flicker) => new(Warm.R, Warm.G * 0.95f, Warm.B, 0.55f * strength * flicker);

    internal static void Paint(RavenPen pen, RavenCandleSpot spot, Vector2 nodeSize, int index, float strength)
    {
        float s = Mathf.Clamp(strength, 0, 1);
        Vector2 foot = new(nodeSize.X / 2, nodeSize.Y), flip = new(spot.Base.X < 960 ? -1 : 1, 1);
        var local = pen.With(new Transform2D(0, flip, 0, foot));
        switch (spot.Kind)
        {
            case RavenCandleKind.Lantern:
                var design = pen.With(new Transform2D(0, foot - spot.Base));
                float floor = RavenLampPaintHooks.GlassBottom - 2;
                RavenLampPaintHooks.LanternLight(design, spot.Base.X, s);
                Wax(design.With(new Transform2D(0, flip, 0, new Vector2(spot.Base.X, floor))), 12, floor - spot.Base.Y, index, s, false);
                RavenLampPaintHooks.LanternSheen(design, spot.Base.X);
                break;
            case RavenCandleKind.Brass:
                Stick(local, s);
                Wax(local.With(new Transform2D(0, new Vector2(0, -StandHeight))), spot.Width, spot.Height, index, s, false);
                Socket(local, spot.Width / 2 + 3, s);
                break;
            default:
                Wax(local, spot.Width, spot.Height, index, s, true);
                break;
        }
    }

    // Body and wax mantle are outlined separately so the overhang casts a readable shadow on the shaft.
    private static void Wax(RavenPen pen, float w, float h, int index, float strength, bool floor)
    {
        float r = w / 2;
        var face = ColdFace.Lerp(Bone, strength);
        var shade = ColdShade.Lerp(Honey, strength);
        var light = ColdLip.Lerp(Ivory, strength);
        if (floor)
        {
            float baseDepth = 0.16f * w;
            pen.Glow(new Vector2(0, baseDepth * 0.1f), new Vector2(w * 1.7f, w * 0.42f), new Color(Warm, 0.3f * strength));
            pen.Glow(new Vector2(0, -baseDepth * 0.1f), new Vector2(r * 1.55f, baseDepth * 1.6f), Shadow, 20);
            Vector2[] puddle = [new(-1.35f * r, -3), new(-0.83f * r, -5.5f), new(0.75f * r, -5),
                new(1.39f * r, -3), new(1.42f * r, -0.8f), new(0.86f * r, 1), new(0.36f * r, 0.8f),
                new(-0.18f * r, 2.4f), new(-1.15f * r, 1), new(-1.42f * r, -0.8f)];
            pen.Ink(puddle, face.Lerp(shade, 0.35f), 1.9f);
            pen.Stroke([new(-1.21f * r, -1), new(-0.69f * r, 0.3f)], light, 1.4f);
        }

        Vector2 P(float x, float y) => new(x * r, (y - 1) * h);
        Vector2[] body = [P(-0.91f, 0.01f), P(0.07f, 0), P(0.93f, 0.01f), P(0.89f, 0.27f),
            P(0.84f, 0.64f), P(0.98f, 0.92f), P(0.98f, 0.97f), P(0.53f, 1), P(-0.41f, 0.99f),
            P(-1.04f, 0.96f), P(-0.88f, 0.79f), P(-0.91f, 0.55f), P(-0.87f, 0.28f)];
        pen.Ramp(body, face, face.Lerp(shade, 0.45f), P(-0.7f, 0.2f), P(1, 0.9f));
        pen.Fill([P(0.46f, 0.06f), body[2], body[3], body[4], body[5], body[6], body[7], P(0.13f, 0.993f),
            P(0.42f, 0.84f), P(0.22f, 0.67f), P(0.52f, 0.39f)], shade);
        pen.Fill([P(-0.69f, 0.26f), P(-0.42f, 0.33f), P(-0.50f, 0.61f), P(-0.65f, 0.74f)], light);
        pen.Stroke(body, Ink, w < 16 ? 1.6f : 2.8f, true);
        MeltedRim(pen, r, h, index, strength, face, shade, light);
    }

    private static void MeltedRim(RavenPen pen, float r, float h, int index, float strength, Color face, Color shade, Color light)
    {
        float longTear = h * (0.37f + 0.08f * (index % 3));
        float shortTear = h * (0.20f + 0.055f * ((index + 1) % 3));
        // The low front lip runs into two lobes. The taller far lip leaves the wick visible between them.
        Vector2 P(float x, float down) => new(x * r, -h + down);
        var cap = RavenPen.Curve([P(-1.02f, 0), P(-0.65f, -0.31f * r), P(0.09f, -0.16f * r),
            P(0.75f, -0.32f * r), P(1.03f, 0.03f * r), P(0.94f, shortTear * 0.57f),
            P(0.76f, shortTear), P(0.52f, shortTear * 0.96f), P(0.39f, 0.56f * r), P(0.04f, 0.46f * r),
            P(-0.18f, longTear * 0.53f), P(-0.12f, longTear * 0.88f), P(-0.28f, longTear),
            P(-0.52f, longTear * 0.94f), P(-0.58f, longTear * 0.46f), P(-0.91f, 0.66f * r)], 3);
        pen.At(new Vector2(0.6f, 1.8f)).Fill(cap, shade.Darkened(0.25f));
        pen.Ramp(cap, light, face, P(-0.65f, 0), P(0.9f, shortTear));
        pen.Stroke(cap, shade.Darkened(0.39f), r < 8 ? 1.2f : 1.8f, true);
        Vector2[] pool = [P(-0.76f, 0), P(-0.47f, -0.10f * r), P(0.12f, 0.01f * r), P(0.69f, -0.12f * r),
            P(0.80f, 0), P(0.37f, 0.18f * r), P(-0.19f, 0.20f * r)];
        pen.Fill(pool, shade.Lerp(Ember, 0.72f * strength));
        pen.Stroke([P(-0.62f, 0.06f * r), P(-0.23f, 0.22f * r), P(0.13f, 0.21f * r)], light, 1.4f);
        pen.Stroke([P(-0.4f, longTear * 0.60f), P(-0.38f, longTear * 0.87f)], light, 1.7f);

        var tip = P(0, -4);
        pen.Stroke([P(-0.10f, 0.07f * r), P(0.06f, -1.6f), tip], Char, 2.3f);
        pen.Fill(Ellipse(tip, 1.3f, 1.3f, 8)[..^1], Char.Lerp(Ember, strength));
        pen.Glow(tip, new Vector2(4.5f, 4.5f), new Color(Ember, 0.7f * strength), 12);
    }

    // Two beaten brass ribs leave an open lancet through the stem, with worn facets on the inner face.
    private static void Stick(RavenPen pen, float strength)
    {
        var edge = WornEdge.Lerp(Gilt, strength);
        var metal = Patina.Lerp(Bronze, strength);
        pen.Glow(new Vector2(0, -4), new Vector2(70, 16), new Color(Warm, 0.26f * strength));
        pen.Glow(new Vector2(0, -5), new Vector2(46, 10), Shadow, 20);
        var reflected = pen.With(new Transform2D(0, new Vector2(-1, 1), 0, Vector2.Zero));
        reflected.Ink(StemRib, metal.Darkened(0.23f), 2.6f);
        reflected.Stroke([new(-5, -87), new(-14, -71), new(-14, -67)], metal.Lightened(0.22f), 2);
        pen.Ink(StemRib, metal, 2.6f);
        pen.Stroke([new(-4, -88), new(-14, -70), new(-14, -67), new(-7, -56)], edge, 2.3f);
        pen.Stroke([new(-4, -47), new(-4, -32)], edge, 1.8f);

        pen.Ramp(Foot, edge, metal.Darkened(0.25f), new Vector2(-28, -14), new Vector2(24, -3));
        pen.Fill([Foot[3], Foot[4], Foot[5], Foot[6], Foot[7], new(2, -11)], metal);
        pen.Fill([Foot[0], new(-19, -9), new(2, -6), Foot[7], Foot[8], Foot[9], Foot[10]], metal.Darkened(0.30f));
        pen.Stroke(Foot, Ink, 3, true);
        pen.Stroke([new(-26, -11), new(-15, -15), new(-8, -24)], edge, 2);

        pen.Ramp(Chalice, edge, metal, new Vector2(-25, -117), new Vector2(18, -104));
        pen.Fill([Chalice[6], Chalice[7], Chalice[8], Chalice[9], Chalice[10], new(2, -111)], metal.Darkened(0.26f));
        pen.Stroke(Chalice, Ink, 2.8f, true);
        pen.Stroke([new(-24, -116), new(-18, -111), new(-10, -109)], edge, 2);
    }

    private static void Socket(RavenPen pen, float half, float strength)
    {
        var edge = WornEdge.Lerp(Gilt, strength);
        var metal = Patina.Lerp(Bronze, strength);
        Vector2[] lip = [new(-half, -126), new(-half * 0.48f, -122), new(0, -121), new(half * 0.87f, -125),
            new(half, -117), new(half * 0.48f, -113), new(0, -112), new(-half * 0.76f, -115)];
        pen.Ramp(lip, edge, metal, lip[0], lip[4]);
        pen.Stroke(lip, Ink, 2.3f, true);
        pen.Stroke([new(-half + 2, -122.5f), new(-half * 0.44f, -119), new(0, -118)], edge, 1.7f);
    }
}

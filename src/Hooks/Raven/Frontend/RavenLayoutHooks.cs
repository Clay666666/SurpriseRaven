using Godot;

namespace SurpriseRaven.Hooks.Raven;

internal enum RavenCandleKind { Pillar, Brass, Lantern }

internal readonly record struct RavenCandleSpot(string Name, Vector2 Base, float Height, float Width, RavenCandleKind Kind);

// Design-space (1920x1080) composition: central double-headed lamp, stepped pedestal,
// candles on both sides, offering ring at the lamp's foot (after the bilibili 2D replica).
internal static class RavenLayout
{
    // The raven's centre of mass when its feet grip the finial top at (960, 276).
    internal static readonly Vector2 Perch = new Vector2(960, 276) - RavenBirdArtHooks.Feet;
    internal static readonly Vector2 Altar = new(960, 932);
    internal static readonly Vector2[] Slots = [new(620, 932), new(850, 932), new(1080, 932), new(1310, 932)];
    // Everything the raven may cover while flying; it stays clear of the HUD corners and banner.
    internal static readonly Rect2 FlightArea = new(600, 150, 740, 880);

    // Indices 0-5 light in order (both sides, bottom to top); 6-9 are always lit.
    internal static readonly RavenCandleSpot[] Candles =
    [
        new("B4", new(1182, 828), 92, 30, RavenCandleKind.Pillar), new("B5", new(738, 828), 88, 30, RavenCandleKind.Pillar),
        new("B", new(1124, 790), 76, 28, RavenCandleKind.Pillar), new("B1", new(796, 790), 80, 28, RavenCandleKind.Pillar),
        new("B2", new(1066, 754), 64, 26, RavenCandleKind.Pillar), new("B3", new(854, 754), 68, 26, RavenCandleKind.Pillar),
        new("StickL", new(612, 872), 46, 22, RavenCandleKind.Brass), new("StickR", new(1308, 872), 46, 22, RavenCandleKind.Brass),
        new("LanternL", new(800, 400), 0, 18, RavenCandleKind.Lantern), new("LanternR", new(1120, 400), 0, 18, RavenCandleKind.Lantern)
    ];

    // Take off, swoop down the left of the lamp, snatch the offering at the ring (index 4, moving
    // right), then climb and recede into the night sky to the upper right.
    internal const int AwayGrab = 4;
    internal static readonly RavenRoutePoint[] AwayRoute =
    [
        new(Perch), new(new(930, 194)), new(new(800, 360), 1.04f), new(new(776, 760), 1.08f), new(new(832, 906), 1.1f),
        new(new(990, 830), 1.06f), new(new(1130, 620), 0.86f), new(new(1225, 440), 0.6f, 0.75f), new(new(1270, 310), 0.42f, 0)
    ];

    // Return out of the sky from the upper left, drop the reward over the ring (index 3, moving
    // right), climb the right side and glide back onto the lamp facing left.
    internal const int ReturnDrop = 3;
    internal static readonly RavenRoutePoint[] ReturnRoute =
    [
        new(new(660, 300), 0.42f, 0), new(new(700, 430), 0.62f, 0.8f), new(new(760, 640), 0.92f), new(new(850, 820), 1.08f),
        new(new(1040, 700), 1.04f), new(new(1150, 500)), new(new(1070, 290)), new(Perch)
    ];

    // First arrival: a short glide in from the right, landing on the finial.
    internal static readonly RavenRoutePoint[] IntroRoute =
        [new(new(1290, 215), 0.5f, 0), new(new(1190, 214), 0.75f, 0.9f), new(new(1060, 222), 0.95f), new(Perch)];
}

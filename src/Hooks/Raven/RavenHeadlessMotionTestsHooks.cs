using Godot;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    // Shorter, calmer, seamless raven flight at several frame rates, plus the reduced-motion mode.
    private static void TestRavenMotion()
    {
        var timings = RavenFrontendConfigHooks.Current.Timings;
        List<RavenFlightReport> round = [];
        foreach (bool reduced in new[] { false, true })
            foreach (double fps in new[] { 30.0, 60.0, 144.0 })
            {
                List<RavenFlightReport> reports = [];
                float farthest = 0, spread = 0;
                var failures = RavenMotionChecksHooks.Run(timings, fps, reduced, reports, (_, bird) =>
                {
                    if (!bird.Visible) return;
                    farthest = Math.Max(farthest, bird.Position.DistanceTo(RavenLayout.Perch));
                    spread = Math.Max(spread, bird.Pose.Flight);
                });
                Check(failures.Count == 0, string.Join("; ", failures));
                if (reduced) Check(farthest < 8 && spread < 0.01f, $"Reduced motion keeps the raven folded on the lamp ({farthest:0.0} px).");
                if (!reduced && fps == 60) round = reports;
            }
        float away = new RavenFlightPath(RavenLayout.AwayRoute, timings.FlyAway, 0, 0, RavenLayout.AwayGrab, (float)timings.GrabFraction).Length;
        float back = new RavenFlightPath(RavenLayout.ReturnRoute, timings.ReturnFly, 0, 0, RavenLayout.ReturnDrop,
            (float)(timings.DropAt / timings.ReturnFly)).Length;
        float intro = new RavenFlightPath(RavenLayout.IntroRoute, RavenBirdCuesHooks.IntroSeconds, 0, 0).Length;
        Check(away is >= 0.5f * RavenMotionChecksHooks.BeforeAwayLength and <= 0.7f * RavenMotionChecksHooks.BeforeAwayLength,
            $"Away route is 30-50% shorter ({away:0} px).");
        Check(back is >= 0.5f * RavenMotionChecksHooks.BeforeReturnLength and <= 0.7f * RavenMotionChecksHooks.BeforeReturnLength,
            $"Return route is 30-50% shorter ({back:0} px).");
        Check(intro <= 0.7f * RavenMotionChecksHooks.BeforeIntroLength, $"Intro flight is shorter ({intro:0} px).");
        var flight = round.Single(r => r.Segment == "round");
        Check(flight.WingHz is >= 0.60f * RavenMotionChecksHooks.BeforeWingHz and <= 0.75f * RavenMotionChecksHooks.BeforeWingHz,
            $"Wingbeat is 25-40% slower ({flight.WingHz:0.00} Hz).");
        Check(flight.PeakSpeed <= RavenMotionChecksHooks.MaxSpeed && flight.MaxAccel <= RavenMotionChecksHooks.MaxAccel,
            "Flight speed and acceleration stay bounded.");
        Logger.Log($"[Raven] Motion: away {away:0}px, return {back:0}px, intro {intro:0}px, wing {flight.WingHz:0.00}Hz, "
            + $"peak {flight.PeakSpeed:0}px/s, accel {flight.MaxAccel:0}px/s^2, bounds {flight.Bounds}.");
    }
}

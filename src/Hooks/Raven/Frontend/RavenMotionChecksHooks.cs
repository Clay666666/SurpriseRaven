using Godot;

namespace SurpriseRaven.Hooks.Raven;

internal sealed record RavenFlightReport(string Segment, float Length, float Duration, float PeakSpeed, float MaxAccel,
    float MaxStep, float MaxTurnStep, float MaxFacingStep, float WingHz, Rect2 Bounds);

// Engine-free motion acceptance. Simulates the stage's cue order at a fixed frame rate (bird update
// first, then cues, exactly like RavenStageHooks.Tick) and reports travel, smoothness and placement.
internal static class RavenMotionChecksHooks
{
    // Pre-polish baseline: uniform Catmull-Rom routes and a constant 6 Hz sine wingbeat.
    internal const float BeforeAwayLength = 2704, BeforeReturnLength = 2343, BeforeIntroLength = 2343, BeforeWingHz = 6;
    internal const float MaxSpeed = 1400, MaxAccel = 9000;
    internal static readonly Vector2 StakeCenter = RavenLayout.Altar - new Vector2(0, 22);

    internal static List<string> Run(RavenTimings timings, double fps, bool reduced, List<RavenFlightReport>? reports = null,
        Action<double, RavenBirdMotion>? frame = null)
    {
        List<string> failures = [];
        void Check(bool ok, string message) { if (!ok && failures.Count < 24) failures.Add($"[{fps:0} fps{(reduced ? ", reduced" : "")}] {message}"); }
        var bird = new RavenBirdMotion(RavenLayout.Perch) { ReducedMotion = reduced };
        double dt = 1 / fps, clock = 0;
        Vector2? grab = null, drop = null;
        double landedAt = -1;
        var track = new Track();

        void Step(RavenTimeline? timeline)
        {
            var before = (bird.Position, bird.Rotation, bird.Scale.X, bird.Alpha, bird.WingPhase, bird.Visible);
            bird.Update(dt);
            clock += dt;
            track.Add(bird, before, dt, Check);
            frame?.Invoke(clock, bird);
            if (timeline is null) return;
            foreach (var cue in timeline.Advance(dt))
            {
                RavenBirdCuesHooks.Apply(bird, cue, timings);
                if (cue == RavenCue.Grab) grab = bird.Transform * RavenBirdArtHooks.BeakTip;
                if (cue == RavenCue.Drop) drop = bird.Transform * RavenBirdArtHooks.BeakTip;
                if (cue == RavenCue.Landed) landedAt = clock;
            }
        }

        RavenBirdCuesHooks.Intro(bird);
        track.Begin("intro");
        while (clock < RavenBirdCuesHooks.IntroSeconds + 0.8) Step(null);
        Check(bird.Settled, "Intro flight settles onto the perch.");
        CheckIdle("after intro");
        var round = new RavenTimeline(timings, swallow: false, gainsLight: true);
        foreach (var cue in round.Advance(0)) RavenBirdCuesHooks.Apply(bird, cue, timings);
        track.Begin("round");
        while (!round.Finished) Step(round);
        while (clock < landedAt + 0.8) Step(null);
        Check(landedAt > 0 && bird.Settled, "Round ends with the raven settled on the perch.");
        CheckIdle("after landing");
        track.End();
        if (!reduced)
        {
            Check(grab is { } g && g.DistanceTo(StakeCenter) < 36, $"Beak reaches the offering at the grab cue ({grab}).");
            Check(drop is { X: > 860 and < 1180, Y: > 700 and < 880 }, $"Reward drops over the ring ({drop}).");
        }
        reports?.AddRange(track.Reports);
        if (!reduced) failures.AddRange(RunInterruptedIntro(timings, fps));
        return failures;

        void CheckIdle(string when)
        {
            float lift = bird.Pose.Lift, drift = 0;
            for (double end = clock + 2; clock < end;)
            {
                Step(null);
                drift = Math.Max(drift, bird.Position.DistanceTo(RavenLayout.Perch));
            }
            Check(Mathf.Abs(bird.Pose.Lift - lift) < 0.01f && bird.Pose.Flight < 0.01f, $"Wings stay folded {when}.");
            Check(drift < 6.5f, $"Perched raven stays on the finial {when} (drift {drift:0.0}).");
        }
    }

    // A stake made while the intro is still in the air re-routes the raven without a jump.
    private static List<string> RunInterruptedIntro(RavenTimings timings, double fps)
    {
        List<string> failures = [];
        var bird = new RavenBirdMotion(RavenLayout.Perch);
        var track = new Track();
        track.Begin("interrupted intro");
        RavenBirdCuesHooks.Intro(bird);
        double dt = 1 / fps;
        for (double t = 0; t < 2.4; t += dt)
        {
            if (Math.Abs(t - 0.9) < dt / 2) RavenBirdCuesHooks.Apply(bird, RavenCue.Takeoff, timings);
            var before = (bird.Position, bird.Rotation, bird.Scale.X, bird.Alpha, bird.WingPhase, bird.Visible);
            bird.Update(dt);
            track.Add(bird, before, dt, (ok, message) => { if (!ok && failures.Count < 8) failures.Add($"[{fps:0} fps] {message}"); });
        }
        return failures;
    }

    private sealed class Track
    {
        private string _name = "";
        private float _length, _time, _peak, _accel, _step, _turn, _facing, _phase, _flapTime, _lastSpeed = -1;
        private Rect2? _bounds;
        internal List<RavenFlightReport> Reports { get; } = [];

        internal void Begin(string name) { End(); _name = name; }

        internal void End()
        {
            if (_time > 0) Reports.Add(new(_name, _length, _time, _peak, _accel, _step, _turn, _facing, _flapTime > 0 ? _phase / Mathf.Tau / _flapTime : 0, _bounds ?? default));
            _length = _time = _peak = _accel = _step = _turn = _facing = _phase = _flapTime = 0;
            _lastSpeed = -1;
            _bounds = null;
        }

        internal void Add(RavenBirdMotion bird, (Vector2 At, float Rotation, float FacingX, float Alpha, float Phase, bool Visible) before,
            double delta, Action<bool, string> check)
        {
            float dt = (float)delta;
            if (!bird.Visible || !before.Visible) { _lastSpeed = -1; return; }
            float step = bird.Position.DistanceTo(before.At), speed = step / dt;
            check(step <= MaxSpeed * dt + 2, $"{_name}: no position jump ({step:0.0} px in one frame).");
            check(Mathf.Abs(bird.Rotation - before.Rotation) <= 6f * dt + 0.002f, $"{_name}: pitch changes smoothly.");
            check(Mathf.Abs(bird.Scale.X - before.FacingX) <= 15f * dt + 0.01f, $"{_name}: facing turns without flipping.");
            check(Mathf.Abs(bird.Alpha - before.Alpha) <= 4.8f * dt + 0.002f, $"{_name}: opacity fades smoothly ({before.Alpha:0.000}->{bird.Alpha:0.000} at {bird.Position}).");
            if (bird.Alpha > 0.05f)
                check(RavenLayout.FlightArea.HasPoint(bird.Position), $"{_name}: raven stays inside the flight area ({bird.Position}).");
            if (bird.Flying)
            {
                _length += step;
                _time += dt;
                _phase += (bird.WingPhase - before.Phase + Mathf.Tau) % Mathf.Tau;
                _flapTime += dt;
                _bounds = _bounds is { } b ? b.Expand(bird.Position) : new Rect2(bird.Position, Vector2.Zero);
            }
            if (_lastSpeed >= 0) _accel = Math.Max(_accel, Mathf.Abs(speed - _lastSpeed) / dt);
            _lastSpeed = speed;
            _peak = Math.Max(_peak, speed);
            _step = Math.Max(_step, step);
            _turn = Math.Max(_turn, Mathf.Abs(bird.Rotation - before.Rotation));
            _facing = Math.Max(_facing, Mathf.Abs(bird.Scale.X - before.FacingX));
            check(_accel <= MaxAccel, $"{_name}: acceleration {_accel:0} px/s^2 exceeds {MaxAccel} (speed {speed:0} at {bird.Position}, flying {bird.Flying}).");
        }
    }
}

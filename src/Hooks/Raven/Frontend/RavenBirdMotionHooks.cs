using Godot;

namespace SurpriseRaven.Hooks.Raven;

// Articulation of the painted raven: Flight blends perched (folded) into airborne (spread) wings.
internal struct RavenBirdPose { internal float Flight, Lift, Fold, Head, Beak, Blink, Ruffle, Tail, Legs, Crouch; }

// Engine-free raven motion shared by the stage and the offline preview. Wingbeats accumulate phase,
// so frequency and amplitude changes never jump; body pitch, bob and facing follow the flight path.
internal sealed class RavenBirdMotion
{
    internal const float CruiseHz = 3.6f, TakeoffHz = 4.0f, FlareHz = 2.4f, TurnRate = 9f;
    private readonly Random _random;
    private RavenFlightPath? _path;
    private bool _landing;
    private double _flightTime, _idle, _caw, _crouch, _fade, _phase, _nextLook = 2, _nextBlink = 3, _nextRuffle = 7;
    private float _amp, _mid, _hz = CruiseHz, _pitch, _speed, _accel, _lookTarget, _alphaFrom = 1, _alphaTo = 1, _bob;
    private RavenBirdPose _pose;

    internal RavenBirdMotion(Vector2 perch, int seed = 7919) { PerchPoint = Position = perch; _random = new Random(seed); }

    internal Vector2 PerchPoint { get; }
    internal Vector2 Position { get; private set; }
    internal float Depth { get; private set; } = 1;
    internal float Facing { get; private set; } = 1;
    internal float Rotation { get; private set; }
    internal float Alpha { get; private set; } = 1;
    internal bool Visible { get; private set; } = true;
    internal bool ReducedMotion { get; set; }
    internal bool Flying => _path is not null;
    internal bool Settled => _path is null && _pose.Flight < 0.01f;
    internal RavenBirdPose Pose => _pose;
    internal float WingPhase => (float)_phase;
    internal float WingHz => _hz;
    internal Vector2 Scale => new(Facing * Depth, Depth);
    internal Transform2D Transform => new(Rotation, Scale, 0, Position);

    internal void Perch()
    {
        // Already arriving at the perch: let the landing settle instead of snapping.
        if (Visible && _path is not null && _landing) return;
        if (Visible && _path is null && Position.DistanceTo(PerchPoint) < 4) return;
        _path = null;
        Visible = true;
        Position = PerchPoint;
        Depth = Alpha = Facing = 1;
        Rotation = _pitch = _amp = 0;
        _pose = default;
        _idle = 0;
    }

    internal void Fly(IReadOnlyList<RavenRoutePoint> route, double duration, int anchorIndex = -1, float anchorAt = 0)
    {
        _landing = route[^1].At.DistanceTo(PerchPoint) < 1;
        _flightTime = 0;
        bool fromPerch = Visible && _path is null && route[0].At.DistanceTo(PerchPoint) < 1;
        bool airborne = Visible && _path is not null && Alpha > 0.05f; // Re-routed mid-flight (e.g. during the intro).
        if (ReducedMotion)
        {
            // No travel: fade out on the lamp or fade in already perched.
            _path = null;
            Visible = true;
            if (!_landing) { _alphaFrom = Alpha; _alphaTo = 0; }
            else { Perch(); Alpha = _alphaFrom = 0; _alphaTo = 1; }
            _fade = 0;
            return;
        }
        var points = route.ToArray();
        // Continue from the current pose: the path carries position, depth and opacity; the bob rides on top.
        if (fromPerch || airborne) points[0] = new RavenRoutePoint(Position - new Vector2(0, _bob), Depth, Alpha);
        float start = fromPerch ? 0 : airborne ? _speed : 220, end = _landing ? 0 : 260;
        _path = new RavenFlightPath(points, duration, start, end, anchorIndex, anchorAt);
        _flightTime = 0;
        if (fromPerch) { _phase = 0; _hz = TakeoffHz; }
        else if (!airborne)
        {
            // Arriving out of the sky: start at the route's first point, already in flight.
            (Position, Depth, Alpha, var velocity) = _path.Sample(0);
            Facing = velocity.X > 0 ? -1 : 1;
            _pitch = Pitch(velocity, 0);
            Rotation = _pitch * Facing;
            _pose.Flight = 1;
            _amp = 0.6f;
            _phase = 0;
        }
        Visible = true;
    }

    internal void Away() { _path = null; Visible = false; _bob = 0; }
    internal void Caw() => _caw = 0.45;
    internal void Anticipate(double duration) => _crouch = Math.Max(0.1, duration);

    internal void Update(double delta)
    {
        float dt = (float)Math.Clamp(delta, 0, 0.1);
        _caw = Math.Max(0, _caw - dt);
        _pose.Beak = _caw > 0 ? Mathf.Sin((float)(_caw / 0.45) * Mathf.Pi) : 0;
        if (ReducedMotion && _fade < 1)
        {
            _fade = Math.Min(1, _fade + dt / 0.4);
            Alpha = Mathf.Lerp(_alphaFrom, _alphaTo, Mathf.SmoothStep(0, 1, (float)_fade));
        }
        if (!Visible) return;
        if (_path is not null) UpdateFlight(dt);
        else UpdatePerched(dt);
    }

    private void UpdateFlight(float dt)
    {
        _flightTime += dt;
        float tau = (float)Math.Min(1, _flightTime / _path!.Duration);
        var (at, depth, alpha, velocity) = _path.Sample(tau);
        float speed = velocity.Length();
        _accel = Lag(_accel, (speed - _speed) / Math.Max(dt, 1e-3f), dt, 0.15f);
        _speed = speed;
        float climb = speed > 1 ? Mathf.Clamp(-velocity.Y / speed, -1, 1) : 0;
        float demand = Mathf.Clamp(0.5f + 0.75f * climb + _accel / 900f, 0, 1);
        if (_flightTime < 0.45) demand = 1;
        float remaining = (1 - tau) * _path.Duration;
        float flare = _landing ? Mathf.SmoothStep(0, 1, 1 - remaining / 0.5f) : 0;
        float hz = Mathf.Lerp(_flightTime < 0.45 ? TakeoffHz : CruiseHz, FlareHz, flare);
        float amp = Mathf.Lerp(Mathf.Lerp(0.08f, 1f, Mathf.SmoothStep(0, 1, demand)), 0.45f, flare);
        float mid = Mathf.Lerp(Mathf.Lerp(-0.05f, 0.1f, demand), 0.6f, flare);
        _hz = Lag(_hz, hz, dt, 0.2f);
        _amp = Lag(_amp, amp, dt, 0.12f);
        _mid = Lag(_mid, mid, dt, 0.12f);
        _phase = (_phase + Mathf.Tau * _hz * dt) % Mathf.Tau;
        float stroke = Stroke((float)_phase);
        _pose.Flight = Lag(_pose.Flight, 1, dt, 0.05f);
        _pose.Lift = Mathf.Clamp(_mid + _amp * Mathf.Cos(stroke), -1, 1);
        _pose.Fold = Mathf.Clamp(0.05f + 0.42f * _amp * (0.5f - 0.5f * Mathf.Sin(stroke)), 0, 1);
        _pose.Legs = Lag(_pose.Legs, flare, dt, 0.1f);
        _pose.Crouch = Lag(_pose.Crouch, 0, dt, 0.08f);
        if (Mathf.Abs(velocity.X) > 30) Facing = Mathf.MoveToward(Facing, velocity.X < 0 ? 1 : -1, dt * TurnRate);
        _pitch = Lag(_pitch, Pitch(velocity, flare), dt, 0.12f);
        Rotation = (_pitch - 0.08f * _pose.Crouch) * Facing;
        _pose.Head = -0.45f * _pitch;
        _pose.Tail = 0.3f * flare - 0.15f * _pitch;
        // Downstroke lifts the body slightly; the bob fades out with the wingbeat amplitude.
        _bob = -4f * _amp * Mathf.Sin(stroke) * depth * (1 - flare);
        Position = at + new Vector2(0, _bob);
        Depth = depth;
        Alpha = alpha;
        if (tau < 1) return;
        if (_landing) { _path = null; _idle = 0; Position = PerchPoint; }
        else Visible = alpha > 0.01f;
    }

    private void UpdatePerched(float dt)
    {
        _idle += dt;
        _amp = Lag(_amp, 0, dt, 0.1f);
        _speed = _accel = 0;
        // Landing settle: wings fold with damping, the body levels and the facing turns back to the lamp.
        _pose.Flight = Lag(_pose.Flight, 0, dt, 0.1f);
        _pose.Lift = Lag(_pose.Lift, 0.15f, dt, 0.08f);
        _pose.Fold = Lag(_pose.Fold, 1, dt, 0.08f);
        _pose.Legs = Lag(_pose.Legs, 0, dt, 0.1f);
        _pose.Tail = Lag(_pose.Tail, 0, dt, 0.15f);
        _pitch = Lag(_pitch, 0, dt, 0.1f);
        Facing = Mathf.MoveToward(Facing, 1, dt * TurnRate);
        Depth = Lag(Depth, 1, dt, 0.1f);
        _crouch = Math.Max(0, _crouch - dt);
        _pose.Crouch = Lag(_pose.Crouch, _crouch > 0 ? 1 : 0, dt, 0.08f);
        if (!ReducedMotion) Alpha = Lag(Alpha, 1, dt, 0.1f);
        Rotation = (_pitch - 0.08f * _pose.Crouch) * Facing;
        float breathe = Mathf.Sin((float)(_idle * Mathf.Tau / 3.4)) * 1.5f * (1 - _pose.Flight);
        _bob = 0;
        Position = PerchPoint + new Vector2(0, breathe + 5 * _pose.Crouch);
        if ((_nextLook -= dt) <= 0) { _lookTarget = Next(-0.22f, 0.28f); _nextLook = Next(2f, 5f); }
        float look = _pose.Crouch > 0.05f ? -0.32f : _lookTarget;
        _pose.Head = Lag(_pose.Head, look, dt, 0.16f);
        if ((_nextBlink -= dt) <= 0) _nextBlink = Next(3f, 5.5f);
        _pose.Blink = _nextBlink < 0.12 ? 1 : 0;
        if ((_nextRuffle -= dt) <= 0) _nextRuffle = Next(6f, 10f);
        _pose.Ruffle = _nextRuffle < 0.35 && !ReducedMotion ? Mathf.Sin((float)(_nextRuffle / 0.35) * Mathf.Pi) : 0;
    }

    // Nose up while climbing, down while diving; fades out at low speed and lifts for the landing flare.
    private static float Pitch(Vector2 velocity, float flare)
    {
        float noseUp = Mathf.Atan2(-velocity.Y, Mathf.Max(Mathf.Abs(velocity.X), 1));
        return Mathf.Clamp(noseUp * 0.5f, -0.5f, 0.42f) * Mathf.SmoothStep(0, 220, velocity.Length()) + 0.28f * flare;
    }

    // Downstroke takes 55 percent of the cycle; cos(stroke) runs from +1 (wings up) to -1 (wings down).
    internal static float Stroke(float phase)
    {
        const float down = Mathf.Tau * 0.55f;
        return phase < down ? Mathf.Pi * phase / down : Mathf.Pi + Mathf.Pi * (phase - down) / (Mathf.Tau - down);
    }

    private static float Lag(float value, float target, float dt, float tau) => value + (target - value) * (1 - Mathf.Exp(-dt / tau));
    private float Next(float min, float max) => min + (float)_random.NextDouble() * (max - min);
}

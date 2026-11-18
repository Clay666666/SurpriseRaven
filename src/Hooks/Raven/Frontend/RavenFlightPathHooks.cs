using Godot;

namespace SurpriseRaven.Hooks.Raven;

// Control point of a flight route in design space; Scale fakes depth, Alpha fades into the sky.
internal readonly record struct RavenRoutePoint(Vector2 At, float Scale = 1, float Alpha = 1);

// Arc-length parameterised route with a monotone cubic time warp: the bird starts from rest,
// slows where it must pass an anchor (grab, drop) at a scheduled moment and can brake to land.
internal sealed class RavenFlightPath
{
    private const int Steps = 24;
    private readonly Vector2[] _points;
    private readonly float[] _scale, _alpha, _distance;
    private readonly (float T, float S, float M)[] _knots;

    internal RavenFlightPath(IReadOnlyList<RavenRoutePoint> route, double duration, float startSpeed, float endSpeed,
        int anchorIndex = -1, float anchorAt = 0, float anchorSlowdown = 0.7f)
    {
        if (route.Count < 2) throw new ArgumentException("A flight route needs two points.");
        Duration = (float)Math.Max(0.05, duration);
        _points = RavenPen.Curve(route.Select(p => p.At).ToArray(), Steps, closed: false);
        _distance = new float[_points.Length];
        for (int i = 1; i < _points.Length; i++) _distance[i] = _distance[i - 1] + _points[i].DistanceTo(_points[i - 1]);
        _scale = Scalars(route.Select(p => p.Scale).ToArray());
        _alpha = Scalars(route.Select(p => p.Alpha).ToArray());
        Length = _distance[^1];
        List<(float T, float S, float M)> knots = [(0, 0, startSpeed * Duration)];
        if (anchorIndex > 0 && anchorIndex < route.Count - 1 && anchorAt is > 0.05f and < 0.95f)
        {
            float s = _distance[anchorIndex * Steps];
            float before = s / anchorAt, after = (Length - s) / (1 - anchorAt);
            knots.Add((anchorAt, s, anchorSlowdown * 2 * before * after / Math.Max(before + after, 1e-3f)));
        }
        knots.Add((1, Length, endSpeed * Duration));
        _knots = [.. knots];
        // Clamp slopes so the warp stays monotone (Fritsch-Carlson): no reversing or stalling mid-route.
        for (int k = 0; k < _knots.Length - 1; k++)
        {
            float secant = (_knots[k + 1].S - _knots[k].S) / (_knots[k + 1].T - _knots[k].T);
            _knots[k].M = Math.Clamp(_knots[k].M, 0, 3 * secant);
            _knots[k + 1].M = Math.Clamp(_knots[k + 1].M, 0, 3 * secant);
        }
    }

    internal float Duration { get; }
    internal float Length { get; }
    internal Vector2 End => _points[^1];

    // Position, depth scale, opacity and velocity (design px/s) at normalised time tau.
    internal (Vector2 Position, float Scale, float Alpha, Vector2 Velocity) Sample(float tau)
    {
        tau = Mathf.Clamp(tau, 0, 1);
        int k = 0;
        while (k < _knots.Length - 2 && tau > _knots[k + 1].T) k++;
        var (t0, s0, m0) = _knots[k];
        var (t1, s1, m1) = _knots[k + 1];
        float h = t1 - t0, x = (tau - t0) / h, x2 = x * x, x3 = x2 * x;
        float s = (2 * x3 - 3 * x2 + 1) * s0 + (x3 - 2 * x2 + x) * h * m0 + (-2 * x3 + 3 * x2) * s1 + (x3 - x2) * h * m1;
        float ds = ((6 * x2 - 6 * x) * s0 + (3 * x2 - 4 * x + 1) * h * m0 + (-6 * x2 + 6 * x) * s1 + (3 * x2 - 2 * x) * h * m1) / h;
        int i = Array.BinarySearch(_distance, Mathf.Clamp(s, 0, Length));
        if (i < 0) i = Math.Max(1, ~i);
        i = Math.Clamp(i, 1, _points.Length - 1);
        float span = Math.Max(_distance[i] - _distance[i - 1], 1e-4f), u = Mathf.Clamp((s - _distance[i - 1]) / span, 0, 1);
        var tangent = (_points[i] - _points[i - 1]) / span;
        return (_points[i - 1].Lerp(_points[i], u), Mathf.Lerp(_scale[i - 1], _scale[i], u),
            Mathf.Lerp(_alpha[i - 1], _alpha[i], u), tangent * ds / Duration);
    }

    // Catmull-Rom on per-point scalars, evaluated by arc length within each segment so depth and
    // opacity change at a rate proportional to the bird's speed.
    private float[] Scalars(float[] values)
    {
        int n = values.Length;
        var result = new float[(n - 1) * Steps + 1];
        for (int seg = 0; seg < n - 1; seg++)
        {
            float a = values[Math.Max(0, seg - 1)], b = values[seg], c = values[seg + 1], d = values[Math.Min(n - 1, seg + 2)];
            for (int k = 0; k < Steps; k++)
            {
                float from = _distance[seg * Steps], span = Math.Max(_distance[(seg + 1) * Steps] - from, 1e-4f);
                float u = (_distance[seg * Steps + k] - from) / span;
                result[seg * Steps + k] = 0.5f * (2 * b + (c - a) * u + (2 * a - 5 * b + 4 * c - d) * u * u + (3 * b - a - 3 * c + d) * u * u * u);
            }
        }
        result[^1] = values[^1];
        return result;
    }
}

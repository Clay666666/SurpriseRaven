namespace SurpriseRaven.Hooks.Raven;

public enum RavenCue { Offer, Takeoff, Grab, Gone, ReturnStart, Drop, Reveal, Landed, Extinguish, SwallowVoice, Finish, Ignite }

// Pure, frame-rate independent schedule of one round's presentation.
public sealed class RavenTimeline
{
    private readonly IReadOnlyList<(double At, RavenCue Cue)> _schedule;
    private int _next;

    public RavenTimeline(RavenTimings timings, bool swallow)
        : this(swallow, Schedule(timings, swallow)) { }

    public RavenTimeline(RavenTimings timings, bool swallow, bool gainsLight)
        : this(swallow, Schedule(timings, swallow, gainsLight)) { }

    private RavenTimeline(bool swallow, IReadOnlyList<(double At, RavenCue Cue)> cues)
    {
        Swallow = swallow;
        _schedule = cues;
    }

    public bool Swallow { get; }
    public double Elapsed { get; private set; }
    public bool Finished => _next >= _schedule.Count;

    public static IReadOnlyList<(double At, RavenCue Cue)> Schedule(RavenTimings t, bool swallow) =>
        ScheduleRound(t, swallow, t.Away, gainsLight: false);

    public static IReadOnlyList<(double At, RavenCue Cue)> Schedule(RavenTimings t, bool swallow, bool gainsLight) =>
        ScheduleRound(t, swallow, gainsLight ? t.Away : 0, gainsLight);

    private static IReadOnlyList<(double At, RavenCue Cue)> ScheduleRound(RavenTimings t, bool swallow, double away, bool gainsLight)
    {
        double takeoff = t.OfferFly + t.Crouch;
        List<(double, RavenCue)> cues =
        [
            (0, RavenCue.Offer), (takeoff, RavenCue.Takeoff), (takeoff + t.FlyAway * t.GrabFraction, RavenCue.Grab),
            (takeoff + t.FlyAway, RavenCue.Gone)
        ];
        if (swallow)
        {
            cues.Add((takeoff + t.SwallowExtinguishAt, RavenCue.Extinguish));
            cues.Add((takeoff + t.SwallowVoiceAt, RavenCue.SwallowVoice));
            cues.Add((takeoff + Math.Max(t.SwallowEnd, t.FlyAway), RavenCue.Finish));
        }
        else
        {
            double gone = takeoff + t.FlyAway;
            double back = gone + away;
            if (gainsLight)
            {
                double ignite = gone + t.IgniteStagger;
                cues.Add((ignite, RavenCue.Ignite));
                // Give the flame time to build even when custom timings shorten the bird's absence.
                back = Math.Max(back, ignite + 0.25);
            }
            double reveal = back + t.DropAt + t.DropFall;
            double landed = back + t.ReturnFly;
            cues.Add((back, RavenCue.ReturnStart));
            cues.Add((back + t.DropAt, RavenCue.Drop));
            cues.Add((reveal, RavenCue.Reveal));
            cues.Add((landed, RavenCue.Landed));
            cues.Add((Math.Max(reveal, landed) + 0.2, RavenCue.Finish));
        }
        return cues.OrderBy(c => c.Item1).ThenBy(c => (int)c.Item2).ToList();
    }

    public double TimeOf(RavenCue cue) => _schedule.First(c => c.Cue == cue).At;

    public List<RavenCue> Advance(double delta)
    {
        if (delta > 0) Elapsed += delta;
        List<RavenCue> fired = [];
        while (_next < _schedule.Count && _schedule[_next].At <= Elapsed + 1e-9) fired.Add(_schedule[_next++].Cue);
        return fired;
    }
}

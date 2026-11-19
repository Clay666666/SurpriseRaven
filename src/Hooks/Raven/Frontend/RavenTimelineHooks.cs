namespace SurpriseRaven.Hooks.Raven;

public sealed class RavenTimings
{
    public double FadeIn { get; set; } = 0.35;
    public double IgniteStagger { get; set; } = 0.12;
    public double OfferFly { get; set; } = 0.45;
    public double Crouch { get; set; } = 0.25;
    public double FlyAway { get; set; } = 2.4;
    public double GrabFraction { get; set; } = 0.42;
    public double Away { get; set; } = 0.8;
    public double ReturnFly { get; set; } = 2.2;
    public double DropAt { get; set; } = 1.0;
    public double SwapSoundLead { get; set; }
    public double DropFall { get; set; } = 0.45;
    public double SwallowExtinguishAt { get; set; } = 1.7;
    public double SwallowVoiceAt { get; set; } = 2.0;
    public double SwallowEnd { get; set; } = 4.2;
}

public enum RavenCue { Offer, Takeoff, Grab, Gone, ReturnStart, Drop, Reveal, Landed, Extinguish, SwallowVoice, Finish, Ignite, SwapSound }

// Pure, frame-rate independent schedule of one round's presentation.
public sealed class RavenTimeline
{
    private readonly IReadOnlyList<(double At, RavenCue Cue)> _schedule;
    private int _next;

    public RavenTimeline(RavenTimings timings, bool swallow)
        : this(swallow, Schedule(timings, swallow)) { }

    public RavenTimeline(RavenTimings timings, bool swallow, bool gainsLight)
        : this(swallow, Schedule(timings, swallow, gainsLight)) { }

    internal RavenTimeline(RavenTimings timings, bool swallow, bool gainsLight, bool swap)
        : this(swallow, Schedule(timings, swallow, gainsLight, swap)) { }

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

    internal static IReadOnlyList<(double At, RavenCue Cue)> Schedule(RavenTimings t, bool swallow, bool gainsLight, bool swap) =>
        ScheduleRound(t, swallow, gainsLight ? t.Away : 0, gainsLight, swap);

    private static IReadOnlyList<(double At, RavenCue Cue)> ScheduleRound(RavenTimings t, bool swallow, double away, bool gainsLight, bool swap = false)
    {
        double takeoff = t.OfferFly + t.Crouch;
        List<(double, RavenCue)> cues =
        [
            (0, RavenCue.Offer), (takeoff, RavenCue.Takeoff), (takeoff + t.FlyAway * t.GrabFraction, RavenCue.Grab),
            (takeoff + t.FlyAway, RavenCue.Gone)
        ];
        double candleChange = takeoff + t.SwallowExtinguishAt;
        if (swallow)
        {
            cues.Add((candleChange, RavenCue.Extinguish));
            cues.Add((takeoff + t.SwallowVoiceAt, RavenCue.SwallowVoice));
            cues.Add((takeoff + Math.Max(t.SwallowEnd, t.FlyAway), RavenCue.Finish));
        }
        else
        {
            double gone = takeoff + t.FlyAway;
            double back = gone + away;
            if (gainsLight)
            {
                cues.Add((candleChange, RavenCue.Ignite));
                // Give the flame time to build even when custom timings shorten the bird's absence.
                back = Math.Max(back, candleChange + 0.25);
            }
            double reveal = back + t.DropAt + t.DropFall;
            double landed = back + t.ReturnFly;
            cues.Add((back, RavenCue.ReturnStart));
            if (swap) cues.Add((back + Math.Max(0, t.DropAt - t.SwapSoundLead), RavenCue.SwapSound));
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

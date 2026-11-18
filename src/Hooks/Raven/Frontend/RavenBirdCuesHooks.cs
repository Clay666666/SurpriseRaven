namespace SurpriseRaven.Hooks.Raven;

// Timeline cues that move the raven. Shared by the stage, the motion checks and the offline preview,
// so all three drive the bird identically.
internal static class RavenBirdCuesHooks
{
    internal const double IntroSeconds = 1.6;

    internal static void Intro(RavenBirdMotion bird)
    {
        bird.Away();
        bird.Fly(RavenLayout.IntroRoute, IntroSeconds);
    }

    internal static void Apply(RavenBirdMotion bird, RavenCue cue, RavenTimings t)
    {
        switch (cue)
        {
            case RavenCue.Offer:
                bird.Anticipate(t.OfferFly + t.Crouch);
                break;
            case RavenCue.Takeoff:
                bird.Caw();
                bird.Fly(RavenLayout.AwayRoute, t.FlyAway, RavenLayout.AwayGrab, (float)t.GrabFraction);
                break;
            case RavenCue.Gone:
                bird.Away();
                break;
            case RavenCue.ReturnStart:
                bird.Fly(RavenLayout.ReturnRoute, t.ReturnFly, RavenLayout.ReturnDrop, (float)(t.DropAt / t.ReturnFly));
                break;
            case RavenCue.Landed:
                bird.Perch();
                break;
        }
    }
}

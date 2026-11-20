using Godot;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void CheckSwallowView(RavenReplayLoadHooks.Session session, string stakeTitle)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        bool mapOpen = false, mapDeferred = false;
        int shown = 0;
        var audio = new List<string>();
        RavenLampPanelHooks? panel = null;
        void Request() => session.RequestContinuation(() => mapOpen = true, () => mapDeferred = true, message =>
        {
            shown++;
            Check(message.Contains(stakeTitle, StringComparison.Ordinal), "The restored result lost the swallowed relic's name.");
            panel = new RavenLampPanelHooks(session.Restored.Players[0], message, playAudio: audio.Add, restoredSwallow: true);
            panel.AfterOverlayOpened();
        });

        try
        {
            Request();
            Check(shown == 1 && panel is not null && mapDeferred && !mapOpen,
                "Swallow continuation did not request a terminal overlay ahead of the map.");
            Check(!session.CompleteContinuation(false, mapOpen, mapDeferred, panel) && !session.Ready.IsCompleted,
                "Swallow continuation finished before its view was mounted.");
            tree.Root.AddChild(panel!);
            panel!.AfterOverlayShown();
            Check(RavenOverlayHooks.IsMounted(panel) && panel.IsTerminal
                && panel.Stage is { Phase: RavenStagePhase.Ended } stage && stage.Candles.Count == 10
                && stage.Candles.All(candle => !candle.Lit && !candle.Smoking) && audio.Count == 0,
                "The actual mounted terminal panel did not start with ten dark candles and silent playback.");
            Check(session.CompleteContinuation(false, mapOpen, mapDeferred, panel) && session.Ready.IsCompletedSuccessfully,
                "The underlying map was not deferred while the terminal overlay was shown.");
            Check(RavenMapGateHooks.ShouldDefer(true, false, RavenOverlayHooks.IsMounted(panel), false),
                "An active terminal panel did not block map opening.");
            panel.AfterOverlayClosed();
            Check(!RavenMapGateHooks.ShouldDefer(true, false, RavenOverlayHooks.IsMounted(panel), false),
                "Closing the terminal panel did not release the map.");
            Request();
            Check(shown == 1 && !mapOpen, "The same session requested a second terminal overlay.");
        }
        finally
        {
            if (panel is not null && GodotObject.IsInstanceValid(panel)) panel.AfterOverlayClosed();
        }
    }
}

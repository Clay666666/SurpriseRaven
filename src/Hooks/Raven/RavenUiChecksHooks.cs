using Godot;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    internal static async Task<int> RunNodeChecksAsync()
    {
        int checks = RunNodeChecks();
        checks += await RunRelicLayoutChecksAsync();
        checks += await RunNativeDragChecksAsync();
        checks += await RunRewardChecksAsync();
        checks += await RunRewardExitChecksAsync();
        checks += await RunPickupPanelChecksAsync();
        checks += await RunBonusViewChecksAsync();
        checks += await RunRelicGlowChecksAsync();
        checks += await RunRetainedStakeChecksAsync();
        checks += await RunPanelInputChecksAsync();
        checks += await RunHoverChecksAsync();
        checks += await RunStageLayoutChecksAsync();
        return checks;
    }

    // Invoked only by the isolated headless probe; never opens a run or a live overlay.
    internal static int RunNodeChecks()
    {
        if (DisplayServer.GetName() != "headless")
            throw new InvalidOperationException("Raven node checks require headless.");
        int checks = 0;
        foreach (int lights in new[] { 0, 3, 6 })
        {
            var candles = RavenLayout.Candles.Select((spot, i) => new RavenCandleHooks(spot, i)).ToList();
            foreach (var candle in candles) candle.SetLit(candle.Index < 6 ? candle.Index < lights : true, false);
            Require(candles.Count == 10 && candles.Count(c => c.Lit) == lights + 4, $"Ten candles with {lights}+4 lit");
            Require(candles.Take(6).Select(c => c.Lit).SequenceEqual(Enumerable.Range(0, 6).Select(i => i < lights)), "Light order");
            candles[0].SetLit(false, true);
            candles[0].SetLit(true, true);
            Require(candles[0].Lit && !candles[0].Smoking, "Relighting clears smoke");
            candles[9].SetLit(false, true);
            Require(candles[9].Smoking == (RavenAssetsHooks.Texture("tex.smoke") is not null), "Extinguish smoke starts");
            foreach (var candle in candles) candle.Free();
            Require(candles.All(c => !GodotObject.IsInstanceValid(c)), "Candle nodes released");
            checks += 6;
        }
        checks += RunHudChecks();
        var panel = new RavenLampPanelHooks(null!, "headless terminal result", allowStage: false);
        Require(panel.IsTerminal && panel.Stage is null, "Terminal fallback panel construction");
        Require(panel.DefaultFocusedControl is Button { Text: "离开（Esc）" }, "Safe default focus");
        Require(panel.UseSharedBackstop && panel.MouseFilter == Control.MouseFilterEnum.Stop, "Modal input shield requested");
        panel.AfterOverlayClosed();
        Require(panel.IsQueuedForDeletion() && panel.GetParent() is null, "Closed node detached and queued");
        panel.AfterOverlayClosed(); // Idempotent cleanup.
        checks += 4;
        Require(RavenMapGateHooks.ShouldDefer(true, true, false, false), "Pending opportunity delays map");
        Require(RavenMapGateHooks.ShouldDefer(true, false, true, false), "Visible result delays map");
        Require(!RavenMapGateHooks.ShouldDefer(true, false, false, false), "Resolved offers allow map");
        Require(!RavenMapGateHooks.ShouldDefer(false, true, true, false), "Multiplayer map untouched");
        Require(!RavenMapGateHooks.ShouldDefer(true, true, true, true), "Deferred map replays once");
        checks += 5;
        Require(!RavenTransitionHooks.CanTransition, "Headless never runs the room transition");
        checks++;
        checks += RunLayoutChecks();
        checks += RunStageAudioChecks();
        return checks;
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException($"Raven headless UI check failed: {description}");
    }
}

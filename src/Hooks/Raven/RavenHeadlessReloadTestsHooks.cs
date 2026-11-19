using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    private static async Task<int> CheckReloadLifetimesAsync()
    {
        Require(DisplayServer.GetName() == "headless" && !RunManager.Instance.IsInProgress,
            "Reload checks must not start a run.");
        int checks = 0;
        void Check(bool ok, string why) { Require(ok, why); checks++; }
        Player MakePlayer()
        {
            var player = Player.CreateForNewRun<Ironclad>(UnlockState.none, 440UL);
            CreateRewardFixtureRun(player);
            RavenSessionHooks.Enqueue(RavenStorageHooks.Get(player), 2, "same saved room");
            return player;
        }
        var map = new NMapScreen();
        var stack = new NOverlayStack();
        var backstop = new Control();
        stack.AddChild(backstop);
        AccessTools.DeclaredField(typeof(NOverlayStack), "_backstop").SetValue(stack, backstop);
        try
        {
            foreach (var outcome in new[] { "finished", "failed", "canceled", "pending" })
            {
                var old = MakePlayer();
                RavenServiceHooks.ChangeRun(old);
                var panel = new RavenLampPanelHooks(null!, "old fixture", allowStage: false, playAudio: _ => { });
                stack.Push(panel);
                _panel = panel;
                var oldChoice = new TaskCompletionSource<RelicModel>();
                var oldAcquire = new RavenAcquisitionHooks.AcquisitionScope(RavenServiceHooks.BeginObservedAcquisition());
                int staleNotifications = 0;
                var oldObtain = RavenAcquisitionHooks.ObserveObtain(oldChoice.Task, _ => staleNotifications++, oldAcquire);
                var oldPickup = RunServiceOperation(() => RavenServiceHooks.RunRewardExitSettlementFixtureAsync(
                    old, RavenStorageHooks.Get(old), () => oldObtain));
                var oldExit = new TaskCompletionSource();
                ExitWithTransition(() => oldExit.Task);
                RavenMapGateHooks.Defer(map, false);
                RavenStageSession.TimelineActive = true;
                _resultMessage = "old pickup";
                Check(_leaving && InPanelOperation && RavenServiceHooks.Busy && !oldPickup.IsCompleted,
                    "Old native choice owns settlement, exit and panel locks.");
                if (outcome != "failed")
                    RunManager.Instance.CleanUp(); // The patched native method returns immediately without a live run.
                var next = MakePlayer();
                RavenServiceHooks.ChangeRun(next); // Also covers direct replacement without a cleanup notification.
                Check(!RavenServiceHooks.Busy && !InPanelOperation && !_leaving && _resultMessage is null
                    && !RavenStageSession.TimelineActive && !RavenMapGateHooks.HasDeferredMap && !HasOpenPanel
                    && stack.ScreenCount == 0 && !RavenServiceHooks.HasPendingAcquisition,
                    "Loading the same room drops all old locks before the native choice finishes.");
                Check(ReferenceEquals(RavenServiceHooks.CurrentPlayer, next) && RavenServiceHooks.State.HasOpportunity,
                    "The restored player's opportunity survives the handoff.");

                var liveChoice = new TaskCompletionSource<RelicModel>();
                int newAcquisitions = 0;
                var acquire = new RavenAcquisitionHooks.AcquisitionScope(RavenServiceHooks.BeginObservedAcquisition());
                var liveObtain = RavenAcquisitionHooks.ObserveObtain(liveChoice.Task, _ => newAcquisitions++, acquire);
                var pickup = RunServiceOperation(() => RavenServiceHooks.RunRewardExitSettlementFixtureAsync(
                    next, RavenStorageHooks.Get(next), () => liveObtain));
                var currentExit = new TaskCompletionSource();
                ExitWithTransition(() => currentExit.Task);
                RavenStageSession.TimelineActive = true;
                int writes = 0;
                void OnWrite() => writes++;
                RavenServiceHooks.Changed += OnWrite;
                try
                {
                    oldExit.SetResult();
                    if (outcome == "finished") oldChoice.SetResult(ModelDb.Relic<GlassEye>());
                    else if (outcome == "failed") oldChoice.SetException(new InvalidOperationException("old pickup fixture"));
                    else if (outcome == "canceled") oldChoice.SetCanceled();
                    if (outcome != "pending") await oldPickup;
                    Check(RavenServiceHooks.Busy && InPanelOperation && _leaving && RavenStageSession.TimelineActive
                        && RavenServiceHooks.HasPendingAcquisition && !pickup.IsCompleted && _resultMessage is null
                        && staleNotifications == 0 && writes == 0,
                        $"An old {outcome} choice cannot release, refresh or commit against the restored session.");
                    liveChoice.SetResult(ModelDb.Relic<SeaGlass>());
                    await pickup;
                    currentExit.SetResult();
                    Check(!RavenServiceHooks.Busy && !InPanelOperation && !_leaving && !RavenServiceHooks.HasPendingAcquisition && newAcquisitions == 1
                        && RavenServiceHooks.State.HasOpportunity && !RavenServiceHooks.State.Faulted && writes == 1,
                        "The new acquisition, settlement, and normal exit complete independently once.");
                }
                finally { RavenServiceHooks.Changed -= OnWrite; }
            }
        }
        finally
        {
            RavenServiceHooks.ChangeRun(null);
            stack.Free();
            map.Free();
        }
        return checks;
    }
}

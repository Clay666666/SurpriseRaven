using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    internal static async Task<int> RunPickupPanelChecksAsync()
    {
        Require(DisplayServer.GetName() == "headless" && !RunManager.Instance.IsInProgress && _panel is null,
            "Pickup panel checks require isolated headless without a run.");
        var stack = new NOverlayStack();
        var backstop = new Control();
        stack.AddChild(backstop);
        // Keep this stack detached so its native Ready does not bind to a live map.
        typeof(NOverlayStack).GetField("_backstop", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(stack, backstop);
        var room = new PickupCoverFixture();
        List<string> audio = [];
        var lamp = new RavenLampPanelHooks(null!, "pickup fixture", allowStage: false, playAudio: audio.Add);
        stack.Push(room);
        stack.Push(lamp);
        _panel = lamp;
        int checks = 0;
        void Check(bool condition, string message) { Require(condition, message); checks++; }
        try
        {
            Check(audio.SequenceEqual(["entry"]), "Opening the lamp requests one entry audio roll.");
            var player = Player.CreateForNewRun<Ironclad>(UnlockState.none, 32036UL);
            CreateRewardFixtureRun(player);
            foreach (var (relic, benefit) in new (RelicModel Relic, Func<int> Benefit)[]
            {
                (ModelDb.Relic<Strawberry>(), () => (int)player.Creature.MaxHp),
                (ModelDb.Relic<Whetstone>(), () => player.Deck.Cards.Count(c => c.IsUpgradable)),
                (ModelDb.Relic<OldCoin>(), () => player.Gold)
            })
            {
                var candidate = relic.ToMutable();
                candidate.Owner = player;
                int before = benefit();
                int expected = candidate switch { Strawberry => 7, Whetstone => -2, _ => 300 };
                int pickups = 0;
                await RunServiceOperation(async () =>
                {
                    pickups++;
                    await candidate.AfterObtained();
                });
                Check(pickups == 1 && benefit() == before + expected, $"{candidate.Id} applies the original pickup effect once.");
                Check(ReferenceEquals(_panel, lamp) && ReferenceEquals(stack.Peek(), lamp) && lamp.Visible
                    && !lamp.IsQueuedForDeletion(), $"{candidate.Id} keeps the same lamp panel open.");
            }

            foreach (var name in new[] { "card choice", "Glass Eye rewards" })
            {
                var cover = new PickupCoverFixture();
                var complete = new TaskCompletionSource();
                var operation = RunServiceOperation(() => RavenServiceHooks.RunRewardExitSettlementFixtureAsync(
                    player, RavenStorageHooks.Get(player), async () =>
                {
                    stack.Push(cover);
                    await complete.Task;
                }));
                try
                {
                    Refresh();
                    Check(_inPanelOperation && RavenServiceHooks.Busy && !operation.IsCompleted && ReferenceEquals(_panel, lamp)
                        && !lamp.Visible && ReferenceEquals(stack.Peek(), cover), $"{name} covers the existing lamp while pickup waits.");
                    int repeated = 0;
                    await RunServiceOperation(() => { repeated++; return Task.CompletedTask; });
                    Check(repeated == 0, "A second UI operation cannot enter during pickup.");
                    stack.Remove(cover);
                    Check(ReferenceEquals(stack.Peek(), lamp) && lamp.Visible
                        && !lamp.IsQueuedForDeletion(), $"{name} reveals the original lamp after dismissal.");
                    Check(audio.SequenceEqual(["entry"]), $"{name} does not request another entry audio roll on return.");
                }
                finally
                {
                    if (ReferenceEquals(stack.Peek(), cover)) stack.Remove(cover);
                    cover.Free();
                    complete.TrySetResult();
                    await operation;
                }
                Check(!_inPanelOperation && ReferenceEquals(_panel, lamp), "Pickup completion releases the UI lock on the same panel.");
            }

            await RunServiceOperation(() => Task.FromException(new InvalidOperationException("Expected pickup panel fixture failure.")));
            Check(!_inPanelOperation && ReferenceEquals(_panel, lamp) && !lamp.IsQueuedForDeletion(),
                "A failed operation releases the UI lock without closing the lamp.");
        }
        finally
        {
            _panel = null;
            _resultMessage = null;
            stack.Remove(lamp);
            stack.Free();
        }
        return checks;
    }

    private sealed partial class PickupCoverFixture : Control, IOverlayScreen
    {
        public NetScreenType ScreenType => NetScreenType.Rewards;
        public bool UseSharedBackstop => false;
        public Control DefaultFocusedControl => this;
        public Control FocusedControlFromTopBar => this;
        public void AfterOverlayOpened() { }
        public void AfterOverlayShown() => Show();
        public void AfterOverlayHidden() => Hide();
        public void AfterOverlayClosed() => GetParent()?.RemoveChild(this);
    }
}

using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    private static int RunStageAudioChecks()
    {
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.none, 32353UL);
        CreateRewardFixtureRun(player);
        var session = (RavenStageSession.IntroOpportunityId, RavenStageSession.TimelineActive);
        var viewport = new SubViewport { Size = new Vector2I(1920, 1080), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(viewport);
        List<string> audio = [];
        List<AudioStreamPlayer> leads = [];
        int checks = 0;
        void Check(bool ok, string message) { Require(ok, message); checks++; }
        RavenStageHooks stage = null!;
        void Play(string name)
        {
            if (name == "candle.light")
            {
                var candle = stage.Candles[3];
                var sheets = candle.GetChildren().OfType<TextureRect>().ToArray();
                Check(candle.Lit && sheets[0].Modulate.A > 0 && sheets[1].IsVisibleInTree(),
                    "The new flame and its glow are already visible in the frame requesting its sound.");
            }
            if (name != "reward.swap") { audio.Add(name); return; }
            Check(stage.Rewards.Count == 1, "The swap lead starts in the cue that creates the reward slot.");
            RavenAudioHooks.PlaySwapReward((eventName, finished) =>
            {
                audio.Add(eventName);
                if (finished is not null)
                {
                    var lead = new AudioStreamPlayer();
                    viewport.AddChild(lead);
                    leads.Add(lead);
                    RavenAudioHooks.OnFinishedOnce(lead, finished);
                }
                return true;
            });
        }
        stage = new RavenStageHooks(player, null, playAudio: Play);
        viewport.AddChild(stage);
        RelicModel Relic(RelicModel template)
        {
            var relic = template.ToMutable();
            relic.Owner = player;
            return relic;
        }
        var stake = Relic(ModelDb.Relic<RegalPillow>());
        var reward = Relic(ModelDb.Relic<JuzuBracelet>());
        var swap = new RavenRoundPlan(RavenOutcome.Swap, stake, [reward], RelicRarity.Common, 3, 4);
        var state = new RavenState { PendingLights = [3], Lights = 3, OpportunityId = 50 };
        try
        {
            for (int lights = 0; lights <= 5; lights++)
            {
                stage.EnterReady(new RavenState { PendingLights = [lights], Lights = lights, OpportunityId = 30 + lights });
                Check(stage.Candles.All(c =>
                {
                    bool lit = c.Index >= 6 || c.Index < lights;
                    var sheets = c.GetChildren().OfType<TextureRect>().ToArray();
                    return c.Lit == lit && (sheets[0].Modulate.A > 0) == lit && sheets[1].Visible == lit;
                }) && audio.Count == 0, $"A new opportunity with {lights} lights renders silently on its first frame.");
            }

            foreach (bool reduced in new[] { false, true })
            {
                stage.Bird.ReducedMotion = reduced;
                // The second round also exercises a saved Common-to-Common downgrade presented as a swap.
                var plan = reduced ? swap with { Outcome = RavenOutcome.Downgrade } : swap;
                int before = audio.Count;
                stage.EnterReady(state);
                stage.OnCue(RavenCue.Gone, plan);
                Check(!stage.Candles[3].Lit && !stage.Candles[3].GetChildren().OfType<TextureRect>().ElementAt(1).Visible,
                    "The earned flame remains dark when the raven first leaves.");
                stage.OnCue(RavenCue.Ignite, plan);
                stage.OnCue(RavenCue.Ignite, plan);
                stage.OnCue(RavenCue.ReturnStart, plan);
                Check(audio.Skip(before).SequenceEqual(["candle.light"]) && stage.Candles.Count(c => c.Lit) == 8,
                    "One earned candle starts once before the raven returns in both motion modes.");
                stage.OnCue(RavenCue.Drop, plan);
                Check(audio.Skip(before).SequenceEqual(["candle.light", "reward.swap"]), "Only the swap lead starts at Drop.");
                stage.OnCue(RavenCue.Reveal, plan);
                stage.EnterPickup(plan);
                stage.EnterPickup(plan);
                Check(audio.Count == before + 2 && stage.Phase == RavenStagePhase.Pickup && stage.Rewards.Interactive && !stage.IsBusy,
                    "Reveal and pickup refresh do not replay the lead or wait for its completion.");
                int requests = 0;
                void Picked(RavenPickupRequest request) { if (request == new RavenPickupRequest(RavenPickupKind.Candidate, 0)) requests++; }
                stage.Rewards.PickupRequested += Picked;
                var holder = (NTreasureRoomRelicHolder)stage.Rewards.FirstSlot!;
                holder.EmitSignal(NClickableControl.SignalName.Released, holder);
                stage.Rewards.PickupRequested -= Picked;
                Check(requests == 1 && !stage.Rewards.Interactive && audio.Count == before + 2,
                    "The actual reward control accepts pickup before the first clip finishes.");
            }

            Check(leads.Count == 2, "Consecutive swaps each started an independent lead.");
            var crows = audio.Count;
            foreach (var lead in leads.AsEnumerable().Reverse())
            {
                lead.EmitSignal(AudioStreamPlayer.SignalName.Finished);
                lead.EmitSignal(AudioStreamPlayer.SignalName.Finished);
                Check(audio.Count == ++crows && audio[^1] == "result.swap", "Each lead finishes with the three-caw clip exactly once.");
            }
            audio.Clear();
            RavenAudioHooks.PlaySwapReward((name, _) => { audio.Add(name); return name != "reward.swap"; });
            Check(audio.SequenceEqual(["reward.swap", "result.swap"]), "An unavailable lead preserves the legacy caw once.");

            audio.Clear();
            stage.EnterReady(state);
            stage.EnterPickup(swap);
            stage.EnterPickup(swap);
            Check(stage.Candles[3].Lit && stage.Rewards.Interactive && audio.Count == 0, "Restoring a pending round shows its earned light and reward silently.");
            stage.SetCandles(6);
            stage.OnCue(RavenCue.Ignite, swap with { LightsBefore = 6, LightsAfter = 6 });
            Check(stage.Candles.All(c => c.Lit) && audio.Count == 0, "A capped light award is silent.");
            stage.EnterEnded("exit fixture");
            Check(stage.Candles.All(c => !c.Lit) && audio.Count == 0, "Voluntary exit extinguishes the stage silently.");
            stage.SetCandles(6);
            stage.OnCue(RavenCue.Extinguish, swap with { Outcome = RavenOutcome.Swallow });
            stage.EnterEnded("swallow fixture");
            Check(stage.Candles.All(c => !c.Lit && c.Smoking) && audio.SequenceEqual(["candle.out"]),
                "Swallow starts every extinguish in the same call with one sound, including terminal cleanup.");
            audio.Clear();
            var uncommon = Relic(ModelDb.AllRelics.First(r => r.Rarity == RelicRarity.Uncommon));
            var downgrade = swap with { Outcome = RavenOutcome.Downgrade, Stake = uncommon, LightsAfter = 3 };
            stage.EnterReady(state);
            stage.OnCue(RavenCue.Drop, downgrade);
            Check(audio.Count == 0, "A real downgrade does not start the swap lead when its reward appears.");
            stage.OnCue(RavenCue.Reveal, downgrade);
            Check(stage.Hud.PopLabel.Text == "物品降档" && audio.SequenceEqual(["result.downgrade"]),
                "An Uncommon-to-Common downgrade retains its message and reveal voice.");
            foreach (var outcome in Enum.GetValues<RavenOutcome>())
            {
                audio.Clear();
                stage.PlayResultAudio(outcome);
                string[] expected = outcome switch
                {
                    RavenOutcome.Upgrade => ["voice.upgrade", "sfx.upgrade"],
                    RavenOutcome.Downgrade => ["voice.downgrade"],
                    RavenOutcome.Bonus => ["voice.bonus"],
                    RavenOutcome.Swallow => ["voice.swallow"],
                    _ => []
                };
                Check(audio.Select(name => RavenAudioHooks.PickClip(name)).SequenceEqual(expected),
                    $"Result {outcome} requests the correct reveal clips.");
            }
        }
        finally
        {
            stage.Shutdown();
            viewport.Free();
            (RavenStageSession.IntroOpportunityId, RavenStageSession.TimelineActive) = session;
        }
        return checks;
    }
}

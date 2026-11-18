using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    // Standalone model and viewport only: no room, live treasure collection, or started run.
    internal static async Task<int> RunRewardChecksAsync()
    {
        Require(DisplayServer.GetName() == "headless", "Reward checks require headless.");
        Require(!RunManager.Instance.IsInProgress, "Reward fixture must not start a run.");
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.none, 32032UL);
        var runState = CreateRewardFixtureRun(player);
        var original = player.Relics.ToArray();
        RelicModel[] candidates = [ModelDb.Relic<Strawberry>().ToMutable(), ModelDb.Relic<LetterOpener>().ToMutable(), ModelDb.Relic<Pocketwatch>().ToMutable()];
        foreach (var relic in candidates) relic.Owner = player;
        var tree = (SceneTree)Engine.GetMainLoop();
        var viewport = new SubViewport
        {
            Size = new Vector2I(1920, 1080), HandleInputLocally = true, GuiDisableInput = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        var rewards = new RavenRewardsHooks(runState) { Position = new Vector2(0, 160), Scale = new Vector2(1, 0.8f) };
        viewport.AddChild(rewards);
        tree.Root.AddChild(viewport);
        int checks = 0, calls = 0;
        RavenPickupRequest? picked = null;
        bool pickupAllowed = true;
        rewards.CanInteract = () => pickupAllowed;
        void CheckReward(bool condition, string message) { Require(condition, message); checks++; }
        async Task Frame() => await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        rewards.PickupRequested += index => { picked = index; calls++; };
        try
        {
            rewards.ShowRewards(candidates, null);
            await Frame();
            CheckReward(rewards.Count == 3 && rewards.FirstSlot is NTreasureRoomRelicHolder, "Three rewards use native treasure holders.");
            var holders = rewards.GetChildren().OfType<Control>().SelectMany(c => c.GetChildren().OfType<NTreasureRoomRelicHolder>()).ToArray();
            CheckReward(holders.Length == 3 && holders.Select(h => h.Relic.Model).SequenceEqual(candidates), "Each native holder initialized after Ready with its own candidate.");
            CheckReward(holders.All(h => !h.VoteContainer.Visible && !h.IsEnabled), "Votes hidden and pickup disabled during reveal.");
            foreach (var holder in holders)
            {
                var visibleGlows = holder.Relic.GetChildren().OfType<GpuParticles2D>().Where(node => node.Visible).ToArray();
                CheckReward(visibleGlows is [NCardUncommonGlow { Emitting: true }], "Every rarity has one native teal glow without duplicate rarity particles.");
                CheckReward(visibleGlows[0].GlobalPosition.IsEqualApprox(holder.Relic.GetGlobalTransform() * (holder.Relic.Size / 2)),
                    "Native teal glow remains centered after page Y compression.");
            }
            holders[0].EmitSignal(NClickableControl.SignalName.Released, holders[0]);
            CheckReward(calls == 0 && player.Relics.SequenceEqual(original), "Revealing a reward does not grant or remove inventory.");
            rewards.Interactive = true;
            pickupAllowed = false;
            holders[1].EmitSignal(NClickableControl.SignalName.Released, holders[1]);
            CheckReward(calls == 0 && rewards.Interactive && holders.All(holder => holder.IsEnabled),
                "Transition/exit gate rejects native selection without locking the pending choices.");
            pickupAllowed = true;
            holders[1].EmitSignal(NClickableControl.SignalName.Focused, holders[1]);
            CheckReward(rewards.HoveredIndex == 1, "Native focus identifies the F/confirm pickup candidate.");
            holders[1].EmitSignal(NClickableControl.SignalName.Unfocused, holders[1]);
            CheckReward(rewards.HoveredIndex == -1, "Native unfocus clears hovered pickup.");
            holders[2].EmitSignal(NClickableControl.SignalName.Released, holders[2]);
            holders[0].EmitSignal(NClickableControl.SignalName.Released, holders[0]);
            CheckReward(calls == 1 && picked == new RavenPickupRequest(RavenPickupKind.Candidate, 2) && !rewards.Interactive && holders.All(h => !h.IsEnabled),
                "Selecting one candidate synchronously disables every slot and rejects duplicate release.");
            CheckReward(player.Relics.SequenceEqual(original), "Display callbacks cannot grant inventory themselves.");

            rewards.Clear();
            holders[2].EmitSignal(NClickableControl.SignalName.Released, holders[2]);
            CheckReward(calls == 1 && rewards.Count == 0 && rewards.FirstSlot is null, "Clear detaches native events immediately.");
            await Frame();
            rewards.ShowRewards(candidates[..1], null);
            rewards.Interactive = true;
            await Frame();
            var single = (NTreasureRoomRelicHolder)rewards.FirstSlot!;
            pickupAllowed = false;
            single.EmitSignal(NClickableControl.SignalName.Released, single);
            CheckReward(calls == 1 && rewards.Interactive && single.IsEnabled,
                "A restored pending reward also rejects selection while its panel gate is closed.");
            pickupAllowed = true;
            var point = single.GetGlobalRect().GetCenter();
            using (var motion = new InputEventMouseMotion { Position = point, GlobalPosition = point }) viewport.PushInput(motion, true);
            await Frame();
            foreach (bool pressed in new[] { true, false })
            {
                using var button = new InputEventMouseButton
                {
                    Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed
                };
                viewport.PushInput(button, true);
            }
            CheckReward(calls == 2 && picked == new RavenPickupRequest(RavenPickupKind.Candidate, 0), "A real viewport mouse click reaches the transformed native reward holder.");
            rewards.Interactive = true;
            rewards.Hide();
            single.EmitSignal(NClickableControl.SignalName.Released, single);
            CheckReward(calls == 2, "Hidden reward display ignores pickup.");
            rewards.Show();
            rewards.Clear();
            await Frame();

            var fallback = new RavenRewardsHooks(runState, () => null);
            viewport.AddChild(fallback);
            int fallbackCalls = 0;
            fallback.PickupRequested += _ => fallbackCalls++;
            fallback.ShowRewards(candidates[..1], null);
            fallback.Interactive = true;
            CheckReward(fallback.FirstSlot is TextureButton, "Unavailable native scene keeps a clickable relic fallback.");
            var fallbackButton = (TextureButton)fallback.FirstSlot!;
            fallbackButton.EmitSignal(BaseButton.SignalName.Pressed);
            fallbackButton.EmitSignal(BaseButton.SignalName.Pressed);
            CheckReward(fallbackCalls == 1 && !fallback.Interactive, "Fallback uses the same single-pickup gate.");
            fallback.Clear();
            fallbackButton.EmitSignal(BaseButton.SignalName.Pressed);
            CheckReward(fallbackCalls == 1, "Fallback cleanup disconnects its pickup callback.");
            fallback.QueueFree();
            CheckReward(!RunManager.Instance.IsInProgress && player.Relics.SequenceEqual(original), "Reward checks leave run and inventory untouched.");
        }
        finally { viewport.Free(); }
        return checks;
    }

    private static IRunState CreateRewardFixtureRun(Player player)
    {
        // ILSpy verified: 113 inserts optional isCustomGame; bind optional arguments by name.
        var method = typeof(RunState).GetMethod("CreateForTest", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(RunState).FullName, "CreateForTest");
        var args = method.GetParameters().Select(parameter => parameter.Name switch
        {
            "players" => (object)new[] { player },
            "seed" => "RAVENREWARDS032",
            _ when parameter.HasDefaultValue => parameter.DefaultValue,
            _ => throw new InvalidOperationException($"Unexpected required fixture parameter {parameter.Name}.")
        }).ToArray();
        return (IRunState)(method.Invoke(null, args) ?? throw new InvalidOperationException("Reward fixture was not created."));
    }
}

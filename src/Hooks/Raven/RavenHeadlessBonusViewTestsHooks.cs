using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    internal static async Task<int> RunBonusViewChecksAsync()
    {
        Require(DisplayServer.GetName() == "headless", "Bonus view checks require headless.");
        var tree = (SceneTree)Engine.GetMainLoop();
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.none, 33033UL);
        var run = CreateRewardFixtureRun(player);
        var original = ModelDb.Relic<GoldenPearl>().ToMutable();
        original.Owner = player;
        RelicModel[] candidates = [ModelDb.Relic<Strawberry>().ToMutable(), ModelDb.Relic<LetterOpener>().ToMutable(), ModelDb.Relic<Pocketwatch>().ToMutable()];
        foreach (var relic in candidates) relic.Owner = player;
        var viewport = new SubViewport { Size = new Vector2I(1920, 1080), HandleInputLocally = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        var rewards = new RavenRewardsHooks(run) { Position = new Vector2(0, 112), Scale = new Vector2(1, 0.8f) };
        viewport.AddChild(rewards);
        tree.Root.AddChild(viewport);
        int checks = 0;
        List<RavenPickupRequest> picked = [];
        rewards.PickupRequested += picked.Add;
        void CheckView(bool condition, string message) { Require(condition, message); checks++; }
        async Task Frame() => await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Dictionary<ModelId, NTreasureRoomRelicHolder> Slots() => rewards.GetChildren().OfType<Control>()
            .Where(root => !root.IsQueuedForDeletion()).SelectMany(root => root.GetChildren().OfType<NTreasureRoomRelicHolder>())
            .Where(holder => !holder.IsQueuedForDeletion()).ToDictionary(holder => holder.Relic.Model.Id);
        void Pick(NTreasureRoomRelicHolder holder) => holder.EmitSignal(NClickableControl.SignalName.Released, holder);
        void CheckChoiceTitle(string expected, IReadOnlyList<RelicModel> visibleCandidates)
        {
            var title = rewards.GetNode<Label>("ChoiceTitle");
            CheckView(rewards.GetChildren().OfType<Label>().Count() == 1 && title.Text == expected
                && title.Visible == (expected.Length > 0), "Bonus has exactly one correctly updated group title.");
            CheckView(title.MouseFilter == Control.MouseFilterEnum.Ignore && title.FocusMode == Control.FocusModeEnum.None,
                "The title cannot intercept pickup or focus navigation.");
            if (expected.Length == 0) return;
            var shown = Slots();
            var bounds = visibleCandidates.Select(relic => ((Control)shown[relic.Id].GetParent()).GetGlobalRect()).ToArray();
            float center = (bounds.Min(rect => rect.Position.X) + bounds.Max(rect => rect.End.X)) / 2;
            CheckView(Mathf.IsEqualApprox(title.GetGlobalRect().GetCenter().X, center)
                && title.GetGlobalRect().End.Y <= bounds.Min(rect => rect.Position.Y),
                "The group title is centered above the actual candidates under page compression.");
        }
        try
        {
            var bonus = new RavenRoundPlan(RavenOutcome.Bonus, null, candidates, default, 0, 1, original);
            const string warning = " · 离开将放弃未领取的遗物";
            CheckView(RavenStageHooks.PickupCaption(bonus) == "取回原件，并任选一件奖励" + warning
                && RavenStageHooks.PickupCaption(bonus with { OriginalReturn = null }) == "任选一件奖励" + warning
                && RavenStageHooks.PickupCaption(bonus with { Rewards = [] }) == "取回原件" + warning,
                "The pickup action and abandonment warning match the remaining bonus groups in either pickup order.");
            rewards.ShowRewards(candidates, RavenLayout.Perch, original, bonus: true, reducedMotion: true);
            var fading = Slots().Values.Select(holder => (Control)holder.GetParent()).ToArray();
            CheckView(fading.Length == 4 && fading.All(root => root.Modulate.A == 0)
                && fading.Select(root => root.Position + root.Size / 2).ToHashSet().SetEquals(RavenLayout.Slots),
                "Reduced-motion drops start transparent at their final slots.");
            var fadeTimer = tree.CreateTimer(0.4);
            await tree.ToSignal(fadeTimer, SceneTreeTimer.SignalName.Timeout);
            CheckView(fading.All(root => Mathf.IsEqualApprox(root.Modulate.A, 1))
                && fading.Select(root => root.Position + root.Size / 2).ToHashSet().SetEquals(RavenLayout.Slots),
                "Reduced-motion rewards fade in fully without travelling.");
            rewards.Interactive = true;
            await Frame();
            var slots = Slots();
            CheckChoiceTitle("三选一！！！", candidates);
            CheckView(slots.Values.All(holder => !holder.GetParent().GetChildren().OfType<Label>().Any()),
                "Original and candidate icons have no individual captions.");
            var focusedCandidate = slots[candidates[1].Id];
            focusedCandidate.GrabFocus();
            await Frame();
            CheckView(ReferenceEquals(viewport.GuiGetFocusOwner(), focusedCandidate), "The second candidate accepts navigation focus.");
            rewards.ShowRewards(candidates, null, original, bonus: true);
            CheckView(ReferenceEquals(viewport.GuiGetFocusOwner(), focusedCandidate)
                && ReferenceEquals(Slots()[candidates[1].Id], focusedCandidate) && rewards.Interactive,
                "Refreshing the same rewards preserves the enabled candidate and its existing focus.");
            var originalHolder = slots[original.Id];
            CheckView(rewards.Count == 4 && slots.Count == 4, "Bonus shows the original and three candidates at once.");
            CheckView(slots.Values.Select(holder => holder.GetGlobalRect().GetCenter().X).Distinct().Count() == 4,
                "Four native pickup hitboxes occupy separate slots.");
            CheckView(originalHolder.Relic.GetChildren().OfType<NCardUncommonGlow>().Count(glow => glow.Visible) == 1,
                "Ancient original receives the same single native teal halo.");
            var candidatePositions = candidates.ToDictionary(relic => relic.Id, relic => slots[relic.Id].GlobalPosition);
            originalHolder.EmitSignal(NClickableControl.SignalName.Focused, originalHolder);
            rewards.PickupHovered();
            CheckView(picked.SequenceEqual([new RavenPickupRequest(RavenPickupKind.Original)]), "F on the original emits an original-return request.");
            Pick(slots[candidates[0].Id]);
            CheckView(picked.Count == 1, "An in-flight original pickup blocks a same-frame bonus click.");
            rewards.ShowRewards(candidates, null, null, bonus: true);
            rewards.Interactive = true;
            var remaining = Slots();
            CheckChoiceTitle("三选一！！！", candidates);
            CheckView(rewards.Count == 3 && candidates.All(relic => ReferenceEquals(slots[relic.Id], remaining[relic.Id])
                && remaining[relic.Id].GlobalPosition.IsEqualApprox(candidatePositions[relic.Id])),
                "Returning the original keeps every candidate node and position intact.");
            Pick(originalHolder);
            Pick(remaining[candidates[1].Id]);
            CheckView(picked.Count == 2 && picked[^1] == new RavenPickupRequest(RavenPickupKind.Candidate, 1),
                "The removed original is inert and a bonus candidate retains its service index.");
            rewards.Clear();
            CheckChoiceTitle("", []);
            await Frame();

            rewards.ShowRewards(candidates, null, original, bonus: true);
            rewards.Interactive = true;
            slots = Slots();
            originalHolder = slots[original.Id];
            var originalPosition = originalHolder.GlobalPosition;
            Pick(slots[candidates[2].Id]);
            rewards.ShowRewards([], null, original, bonus: true);
            rewards.Interactive = true;
            CheckChoiceTitle("", []);
            CheckView(rewards.Count == 1 && ReferenceEquals(rewards.FirstSlot, originalHolder)
                && originalHolder.GlobalPosition.IsEqualApprox(originalPosition),
                "Choosing the bonus first leaves the same original holder waiting in place.");
            originalHolder.EmitSignal(NClickableControl.SignalName.Focused, originalHolder);
            rewards.PickupHovered();
            CheckView(picked.Count == 4 && picked[^1].Kind == RavenPickupKind.Original,
                "The original remains independently collectible after a bonus selection.");
            rewards.Clear();
            await Frame();

            rewards.ShowRewards(candidates, null, original, bonus: true);
            rewards.Interactive = true;
            slots = Slots();
            rewards.ShowRewards(candidates[1..], null, original, bonus: true);
            rewards.Interactive = true;
            CheckChoiceTitle("二选一！！！", candidates[1..]);
            Pick(slots[candidates[2].Id]);
            CheckView(picked[^1] == new RavenPickupRequest(RavenPickupKind.Candidate, 1),
                "Candidate invalidation updates retained callbacks to the new candidate indices.");
            rewards.ShowRewards(candidates[2..], null, original, bonus: true);
            CheckChoiceTitle("", candidates[2..]);
            rewards.ShowRewards(candidates, null, original, bonus: true);
            CheckChoiceTitle("三选一！！！", candidates);
            rewards.ShowRewards(candidates, null, bonus: false);
            CheckChoiceTitle("", candidates);
            rewards.Clear();
            await Frame();
            rewards.ShowRewards([], null, original, bonus: true);
            rewards.Interactive = true;
            CheckView(rewards.Count == 1 && rewards.FirstSlot is NTreasureRoomRelicHolder,
                "Restoring an original-only pending round rebuilds a collectible native holder.");
            CheckView(((Control)rewards.FirstSlot!.GetParent()).Modulate.A == 1,
                "Restoring a pending reward displays it immediately.");
            CheckView(picked.Count == 5, "Displaying or refreshing pending groups does not automatically collect anything.");
        }
        finally { viewport.Free(); }
        return checks;
    }
}

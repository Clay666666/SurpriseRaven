using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    internal static async Task<int> RunRetainedStakeChecksAsync()
    {
        Require(DisplayServer.GetName() == "headless" && !RunManager.Instance.IsInProgress,
            "Retained stake fixture requires isolated headless without a run.");
        var tree = (SceneTree)Engine.GetMainLoop();
        var viewport = new SubViewport { Size = new Vector2I(1920, 1080) };
        var host = new Control();
        viewport.AddChild(host);
        tree.Root.AddChild(viewport);
        int checks = 0;
        void Check(bool condition, string message) { Require(condition, message); checks++; }
        try
        {
            foreach (var outcome in new[] { RavenOutcome.Keep, RavenOutcome.Bonus })
            foreach (bool exit in new[] { false, true })
            {
                var player = Player.CreateForNewRun<Ironclad>(UnlockState.none, 331UL);
                var other = ModelDb.Relic<Circlet>().ToMutable();
                var original = ModelDb.Relic<Circlet>().ToMutable();
                player.AddRelicInternal(other, silent: true);
                player.AddRelicInternal(original, silent: true);
                var otherHolder = NRelicInventoryHolder.Create(other)!;
                var holder = NRelicInventoryHolder.Create(original)!;
                host.AddChild(otherHolder);
                host.AddChild(holder);
                var state = new RavenState { PendingLights = [1] };
                var pending = new RavenPendingRound
                {
                    Outcome = outcome, SettlementVersion = RavenTransactionHooks.CurrentVersion,
                    StakeId = original.Id.ToString(), StakeIndex = player.Relics.ToList().IndexOf(original),
                    StakeSnapshot = original.ToSerializable(),
                    Candidates = outcome == RavenOutcome.Bonus ? [ModelDb.Relic<Strawberry>().ToMutable().ToSerializable()] : []
                };
                int removed = 0, obtained = 0;
                Task Remove() { removed++; player.RemoveRelicInternal(original, silent: true); return Task.CompletedTask; }
                Check(await RavenTransactionHooks.CommitAsync(state, pending, Remove, () => { }),
                    "The retained stake commits without re-awarding the original.");
                using var retained = new RavenRetainedIconHooks(host,
                    () => RavenServiceHooks.GetRetainedStakeForDisplay(player, state),
                    relic => ReferenceEquals(relic, original) ? holder.Relic
                        : ReferenceEquals(relic, other) ? otherHolder.Relic : null);
                Check(!holder.Relic.Visible && !holder.IsEnabled && otherHolder.Relic.Visible && otherHolder.IsEnabled
                    && ReferenceEquals(holder.Relic.Model, original) && removed == 0 && player.Relics.Contains(original),
                    "Only the exact retained stake is hidden during the animation.");
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                state = JsonSerializer.Deserialize<RavenState>(JsonSerializer.Serialize(state))!;
                pending = state.Pending!;
                retained.Refresh();
                Check(ReferenceEquals(RavenServiceHooks.GetRetainedStakeForDisplay(player, state), original)
                    && !holder.Relic.Visible && otherHolder.Relic.Visible, "Restored pending stakes retain the correct hidden icon.");
                if (exit) await RavenTransactionHooks.AbandonAsync(state, Remove, () => { });
                else if (outcome == RavenOutcome.Bonus)
                    Check(RavenTransactionHooks.CollectOriginal(state, pending, true, () => { }) == RavenClaimResult.Claimed
                        && state.Pending is not null, "The original can be returned before the three-choice reward.");
                else
                    Check(await RavenTransactionHooks.CollectAsync(state, pending, 0, _ => true,
                        _ => { obtained++; return Task.CompletedTask; }, () => { }) == RavenClaimResult.Claimed,
                        "The Keep reward returns the existing relic.");
                retained.Refresh();
                bool forfeit = exit && outcome == RavenOutcome.Bonus;
                Check(holder.Relic.Visible && holder.IsEnabled && otherHolder.Relic.Visible
                    && player.Relics.Contains(original) != forfeit && player.Relics.Contains(other)
                    && removed == (forfeit ? 1 : 0) && obtained == 0,
                    "Resolution restores the visual and never replays the original pickup effect.");
                holder.Free();
                otherHolder.Free();
            }
        }
        finally { viewport.Free(); }
        return checks;
    }
}

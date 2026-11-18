using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    internal static async Task<int> RunHoverChecksAsync()
    {
        using var f = new RavenHoverFixtureHooks();
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.none, 171133UL);
        var run = CreateRewardFixtureRun(player);
        var original = ModelDb.Relic<TungstenRod>().ToMutable();
        RelicModel[] candidates = [ModelDb.Relic<Strawberry>().ToMutable(), ModelDb.Relic<Pocketwatch>().ToMutable()];
        original.Owner = player;
        foreach (var relic in candidates) relic.Owner = player;
        var rewards = new RavenRewardsHooks(run);
        f.Host.AddChild(rewards);
        rewards.ShowRewards(candidates, null, original, bonus: true);
        rewards.Interactive = true;
        await f.Frame();
        var originalHolder = (NTreasureRoomRelicHolder)rewards.FirstSlot!;
        var candidateHolders = rewards.GetChildren().OfType<Control>()
            .SelectMany(root => root.GetChildren().OfType<NTreasureRoomRelicHolder>()).Where(h => h != originalHolder).ToArray();
        await f.Move(new Vector2(30, 300));
        RavenFocusHooks.FocusDefault(rewards, originalHolder);
        f.Check(!originalHolder.HasFocus() && !f.HasTip(originalHolder), "Mouse mode does not focus or inspect the original automatically.");
        await f.Move(originalHolder.GetGlobalRect().GetCenter());
        f.Check(f.HasTip(originalHolder) && rewards.HoveredRequest?.Kind == RavenPickupKind.Original,
            "Real pointer entry displays the original holder's own tooltip and pickup target.");
        await f.Move(new Vector2(30, 300));
        f.Check(!f.HasTip(originalHolder) && rewards.HoveredRequest is null, "Pointer exit removes the original tooltip and F target.");

        f.SetMode(InputType.KeyboardOnlyMode);
        RavenFocusHooks.FocusDefault(rewards, originalHolder);
        f.Check(originalHolder.HasFocus() && f.HasTip(originalHolder), "Directional navigation focuses and inspects the original.");
        candidateHolders[1].GrabFocus();
        RavenFocusHooks.FocusDefault(rewards, originalHolder);
        f.Check(candidateHolders[1].HasFocus() && f.HasTip(candidateHolders[1]) && !f.HasTip(originalHolder),
            "Refresh preserves a valid candidate focus and removes the preceding owner's tooltip.");
        f.SetMode(InputType.MouseAndKeyboard);
        await f.Move(candidateHolders[1].GetGlobalRect().GetCenter());
        await f.Move(new Vector2(30, 300));
        f.Check(!candidateHolders[1].HasFocus() && !f.HasTip(candidateHolders[1]),
            "Mouse exit releases a stale programmatic focus instead of retaining its tooltip.");

        await f.Move(originalHolder.GetGlobalRect().GetCenter());
        rewards.ShowRewards(candidates, null, null, bonus: true);
        rewards.Interactive = true;
        RavenFocusHooks.FocusDefault(rewards, rewards.FirstSlot!);
        f.Check(!f.HasTip(originalHolder) && candidateHolders.All(h => !h.IsQueuedForDeletion() && !f.HasTip(h)),
            "Returning the original clears its owner immediately while preserving quiet candidate nodes.");
        await f.Frame();
        await f.Move(candidateHolders[0].GetGlobalRect().GetCenter());
        f.Check(f.HasTip(candidateHolders[0]), "A retained candidate still opens its own tooltip.");
        rewards.Interactive = false;
        f.Check(!f.HasTip(candidateHolders[0]) && rewards.HoveredRequest is null, "Disabling rewards clears tooltip ownership immediately.");
        rewards.Interactive = true;
        await f.Move(new Vector2(30, 300));
        await f.Move(candidateHolders[0].GetGlobalRect().GetCenter());
        rewards.Hide();
        f.Check(!f.HasTip(candidateHolders[0]), "Hidden rewards do not retain tooltips.");
        rewards.Show();
        rewards.Clear();
        await f.Frame();

        rewards.ShowRewards(candidates, null, original, bonus: true);
        rewards.Interactive = true;
        originalHolder = (NTreasureRoomRelicHolder)rewards.FirstSlot!;
        await f.Move(originalHolder.GetGlobalRect().GetCenter());
        rewards.ShowRewards([], null, original, bonus: true);
        rewards.Interactive = true;
        f.Check(ReferenceEquals(rewards.FirstSlot, originalHolder), "Bonus-first pickup retains the same original holder.");
        await f.Move(new Vector2(30, 300));
        f.Check(!f.HasTip(originalHolder), "The remaining original tooltip still closes on pointer exit.");
        rewards.Clear();
        await f.Frame();
        await CheckFallbackHoverAsync(f, run, original);
        await CheckRetainedHoverAsync(f, original);
        return f.Checks;
    }

    private static async Task CheckRetainedHoverAsync(RavenHoverFixtureHooks f, RelicModel original)
    {
        var holder = NRelicInventoryHolder.Create(original)!;
        holder.Position = new Vector2(80, 80);
        f.Host.AddChild(holder);
        await f.Frame();
        await f.Move(holder.GetGlobalRect().GetCenter());
        f.Check(f.HasTip(holder), "Inventory hover uses the inventory holder as owner.");
        RelicModel? pending = original;
        using var retained = new RavenRetainedIconHooks(f.Host, () => pending, _ => holder.Relic);
        f.Check(!holder.Relic.Visible && holder.Visible && !holder.IsEnabled && !f.HasTip(holder),
            "Hiding a pending original clears and disables its holder while retaining its layout slot.");
        await f.Move(new Vector2(30, 300));
        await f.Move(holder.GetGlobalRect().GetCenter());
        f.Check(!f.HasTip(holder), "The invisible pending original cannot recreate an inventory tooltip.");
        pending = null;
        retained.Refresh();
        f.Check(holder.Relic.Visible && holder.IsEnabled, "Claiming the original restores its exact visual and input state.");
        await f.Move(new Vector2(30, 300));
        f.Check(!f.HasTip(holder), "Restored inventory tooltip also closes on pointer exit.");
        holder.Free();
    }
}

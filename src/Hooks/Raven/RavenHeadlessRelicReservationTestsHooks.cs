using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestNativeRoomRelicOffers()
    {
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 68013UL);
        var run = (RunState)CreatePoolFixtureRun(player);
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        var manager = (RunManager)RuntimeHelpers.GetUninitializedObject(typeof(RunManager));
        typeof(RunManager).GetProperty("State", privateInstance)!.SetValue(manager, run);
        var room = new MerchantRoom();
        run.PushRoom(room);
        var inventory = new MerchantInventory(player);
        var opener = ModelDb.Relic<LetterOpener>().ToMutable();
        var restocked = ModelDb.Relic<Anchor>().ToMutable();
        var entry = new MerchantRelicEntry(opener, player);
        var relicEntries = (List<MerchantRelicEntry>)typeof(MerchantInventory)
            .GetField("_relicEntries", privateInstance)!.GetValue(inventory)!;
        relicEntries.Add(entry);

        if (typeof(MerchantRoom).GetProperty("Inventories") is { } inventories)
            ((List<MerchantInventory>)inventories.GetValue(room)!).Add(inventory);
        else
        {
            var merchantSync = typeof(RunManager).GetProperty("MerchantSynchronizer")!;
            var syncType = merchantSync.PropertyType;
            var synchronizer = RuntimeHelpers.GetUninitializedObject(syncType);
            syncType.GetField("_runState", privateInstance)!.SetValue(synchronizer, run);
            syncType.GetField("_inventories", privateInstance)!
                .SetValue(synchronizer, new List<MerchantInventory> { inventory });
            merchantSync.SetValue(manager, synchronizer);
        }
        HashSet<ModelId> Snapshot() => RavenRelicAvailabilityHooks.GetUnavailableRelicIds(player, manager);
        Check(Snapshot().Contains(opener.Id) && !Snapshot().Contains(restocked.Id) && !entry.EnoughGold,
            "The player's Letter Opener stock must be reserved even if unaffordable.");

        var model = typeof(MerchantRelicEntry).GetProperty(nameof(MerchantRelicEntry.Model))!;
        model.SetValue(entry, null);
        Check(!Snapshot().Contains(opener.Id), "A sold-out slot reserved its former stock.");
        model.SetValue(entry, restocked);
        Check(Snapshot().Contains(restocked.Id) && !Snapshot().Contains(opener.Id),
            "Restocking did not replace the temporary reservation.");
        run.PopCurrentRoom();
        Check(!Snapshot().Contains(restocked.Id), "Leaving the shop retained an unbought relic reservation.");

        run.PushRoom(new TreasureRoom(0));
        var chestRelics = new List<RelicModel> { opener, restocked };
        var treasure = RuntimeHelpers.GetUninitializedObject(typeof(TreasureRoomRelicSynchronizer));
        typeof(RunManager).GetProperty(nameof(RunManager.TreasureRoomRelicSynchronizer))!.SetValue(manager, treasure);
        var current = typeof(TreasureRoomRelicSynchronizer).GetField("_currentRelics", privateInstance)!;
        current.SetValue(treasure, chestRelics);
        Check(Snapshot().IsSupersetOf([opener.Id, restocked.Id]), "Both pre-rolled chest relics were not reserved.");
        chestRelics.Remove(opener);
        Check(Snapshot().Contains(restocked.Id) && !Snapshot().Contains(opener.Id),
            "The second chest relic was not reserved after picking the first.");
        var extra = RavenRewardReservationHooks.Track(new RewardsSet(player).WithCustomRewards(
            [CreateRelicReward(opener, player)]));
        try
        {
            Check(Snapshot().IsSupersetOf([opener.Id, restocked.Id]),
                "The chest's extra rewards hid the main treasure reservation.");
        }
        finally { extra.Release(); }
        Check(Snapshot().Contains(restocked.Id), "The main treasure reservation ended with the extra rewards.");
        run.PopCurrentRoom();
        Check(!Snapshot().Contains(restocked.Id), "Leaving treasure retained an unpicked relic reservation.");
        run.PushRoom(new TreasureRoom(0));
        current.SetValue(treasure, null);
        Check(!Snapshot().Contains(restocked.Id), "A cleared chest list retained a reservation.");
        Check(!RunManager.Instance.IsInProgress, "The inventory fixtures started a live run.");
    }

    private static void TestRelicReservations() => TestRelicReservationsAsync().GetAwaiter().GetResult();

    private static async Task TestRelicReservationsAsync()
    {
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 68014UL);
        var run = (RunState)CreatePoolFixtureRun(player);
        run.PushRoom(new TreasureRoom(0));
        run.AppendToMapPointHistory(MapPointType.Treasure, RoomType.Treasure, null);
        var history = run.CurrentMapPointHistoryEntry!.PlayerStats.Single();
        var relicA = ModelDb.Relic<Strawberry>();
        var relicB = ModelDb.Relic<LetterOpener>();
        var rewardA = CreateRelicReward(relicA.ToMutable(), player);
        var rewardB = CreateRelicReward(relicB.ToMutable(), player);
        var outer = new RewardsSet(player).WithCustomRewards(
            [rewardA, new LinkedRewardSet([rewardB], player), new RelicReward(player)]);
        var outerReservation = RavenRewardReservationHooks.Track(outer);
        try
        {
            var selected = typeof(Reward).GetProperty(nameof(Reward.SuccessfullySelected))!;
            selected.SetValue(rewardA, true);
            history.RelicChoices.Add(new ModelChoiceHistoryEntry { choice = relicA.Id, wasPicked = true });
            Check(!RavenPoolHooks.CanClaim(player, relicA.ToMutable()) && !RavenPoolHooks.CanClaim(player, relicB.ToMutable()),
                "After taking A, its history and pending linked B must both block pickup.");
            Check(RavenPoolHooks.CreateCandidates(player, relicB.Rarity, 1000, new Rng(922UL))
                .All(relic => relic.Id != relicB.Id), "Candidate generation emitted pending B.");

            var relicC = ModelDb.Relic<Anchor>();
            var nested = new RewardsSet(player).WithCustomRewards(
                [CreateRelicReward(relicB.ToMutable(), player), CreateRelicReward(relicC.ToMutable(), player)]);
            var innerReservation = RavenRewardReservationHooks.Track(nested);
            Check(!RavenPoolHooks.CanClaim(player, relicC.ToMutable()), "An inner reward was not reserved.");
            var failed = new InvalidOperationException("fixture reward failure");
            var failure = RavenRewardReservationHooks.UntilFinished(Task.FromException(failed), innerReservation);
            try { await failure; }
            catch (InvalidOperationException error) when (ReferenceEquals(error, failed)) { }
            Check(failure.IsFaulted && !RavenPoolHooks.CanClaim(player, relicB.ToMutable())
                && RavenPoolHooks.CanClaim(player, relicC.ToMutable()),
                "An inner failure did not preserve the original fault or the outer reservation.");
            var canceled = RavenRewardReservationHooks.UntilFinished(Task.FromCanceled(new CancellationToken(true)),
                RavenRewardReservationHooks.Track(nested));
            try { await canceled; }
            catch (OperationCanceledException) { }
            Check(canceled.IsCanceled && RavenPoolHooks.CanClaim(player, relicC.ToMutable()),
                "A canceled reward set lost its cancellation or left a reservation.");

            var other = Player.CreateForNewRun<Ironclad>(UnlockState.all, 68014UL);
            CreatePoolFixtureRun(other);
            Check(RavenPoolHooks.CanClaim(other, relicB.ToMutable()), "An unrelated run inherited pending B.");
            await CheckSavedRelicConflict(player, relicB);

            var originalRoom = run.PopCurrentRoom();
            run.PushRoom(new TreasureRoom(0));
            Check(RavenPoolHooks.CanClaim(player, relicB.ToMutable()), "A different room inherited pending B.");
            run.PopCurrentRoom();
            run.PushRoom(originalRoom);

            var all = RavenPoolHooks.UnlockedAncients(player).SelectMany(RavenPoolHooks.AncientRelics)
                .DistinctBy(relic => relic.Id).Select(relic => (Reward)CreateRelicReward(relic.ToMutable(), player)).ToList();
            var ancients = RavenRewardReservationHooks.Track(new RewardsSet(player).WithCustomRewards(all));
            try
            {
                Check(all.Count > 0 && RavenPoolHooks.CreateCandidates(player, RelicRarity.Ancient, 1, new Rng(923UL)).Count == 0
                    && RavenPoolHooks.CreateAncientBonusCandidates(player, new Rng(924UL)).Candidates.Count == 0,
                    "Fully reserved Ancient pools emitted a replacement or bonus.");
            }
            finally { ancients.Release(); }

            await RavenRewardReservationHooks.UntilFinished(Task.CompletedTask, outerReservation);
            Check(RavenPoolHooks.CanClaim(player, relicB.ToMutable()) && RavenPoolHooks.CreateCandidates(
                player, relicB.Rarity, 1000, new Rng(922UL)).Any(relic => relic.Id == relicB.Id),
                "Abandoning B did not release the final reservation.");
            Check(!RavenPoolHooks.CanClaim(player, relicA.ToMutable()), "Completing rewards erased obtained history.");
        }
        finally { outerReservation.Release(); }
    }

    private static RelicReward CreateRelicReward(RelicModel relic, Player player)
    {
        var type = typeof(RelicReward);
        var withRng = type.GetConstructor([typeof(RelicModel), typeof(Player), typeof(Rng)]);
        return (RelicReward)(withRng is not null ? withRng.Invoke([relic, player, null])
            : type.GetConstructor([typeof(RelicModel), typeof(Player)])!.Invoke([relic, player]));
    }

    private static async Task CheckSavedRelicConflict(Player player, RelicModel relic)
    {
        foreach (var outcome in new[] { RavenOutcome.Swap, RavenOutcome.Bonus })
        {
            var (state, pending) = TransactionFixture(outcome);
            pending.Candidates = [relic.ToMutable().ToSerializable()];
            int removals = 0;
            await RavenTransactionHooks.CommitAsync(state, pending, () =>
            {
                removals++;
                return Task.CompletedTask;
            }, () => { });
            var restored = RoundTripPendingState(state);
            var locked = restored.Pending!;
            var result = await RavenTransactionHooks.CollectAsync(restored, locked, 0,
                index => RavenPoolHooks.CanClaim(player, RelicModel.FromSerializable(locked.Candidates[index])),
                _ => throw new InvalidOperationException("Reserved relic granted on restore."), () => { });
            Check(result == RavenClaimResult.Invalidated && removals == (outcome == RavenOutcome.Swap ? 1 : 0),
                "A conflicting saved candidate was granted, rerolled or consumed its input twice.");
            if (outcome == RavenOutcome.Bonus)
                Check(restored.Round == 0 && RavenTransactionHooks.CollectOriginal(restored, locked, true, () => { })
                    == RavenClaimResult.Claimed, "Invalidating the saved bonus blocked original pickup.");
            Check(restored.Round == 1 && restored.Pending is null, "The invalidated round did not finish exactly once.");
        }
    }
}

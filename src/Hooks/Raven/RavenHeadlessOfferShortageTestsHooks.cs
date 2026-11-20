using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static RelicReward CreateRelicReward(RelicModel relic, Player player)
    {
        var type = typeof(RelicReward);
        var withRng = type.GetConstructor([typeof(RelicModel), typeof(Player), typeof(Rng)]);
        return (RelicReward)(withRng is not null ? withRng.Invoke([relic, player, null])
            : type.GetConstructor([typeof(RelicModel), typeof(Player)])!.Invoke([relic, player]));
    }

    private static async Task CheckSavedRelicConflict(Player player, RelicModel conflict)
    {
        foreach (var outcome in new[] { RavenOutcome.Swap, RavenOutcome.Bonus })
        {
            var (state, pending) = TransactionFixture(outcome);
            var original = ModelDb.Relic<StrikeDummy>().ToMutable();
            pending.StakeId = original.Id.ToString();
            pending.StakeSnapshot = original.ToSerializable();
            pending.Candidates = [conflict.ToMutable().ToSerializable()];
            player.AddRelicInternal(original, silent: true);
            await RavenTransactionHooks.CommitAsync(state, pending, () =>
            {
                player.RemoveRelicInternal(original, silent: true);
                return Task.CompletedTask;
            }, () => { });
            var restored = RoundTripPendingState(state);
            var locked = restored.Pending!;
            Check(RavenServiceHooks.HasUnavailableOffer(player, locked), "A conflicting saved offer remained displayable.");
            bool returned = await RavenTransactionHooks.ReturnUnavailableAsync(restored, locked,
                () => RavenServiceHooks.RestorePaidOriginal(player, locked), () => { });
            Check(returned && restored.Pending is null && restored.Round == 1
                && restored.BlockedStakeIds.Contains(original.Id.ToString()), "The invalidated round did not end with a saved stake block.");
            var held = player.Relics.Single(relic => relic.Id == original.Id);
            Check(outcome == RavenOutcome.Swap || ReferenceEquals(held, original), "An unspent original was regenerated.");
            player.RemoveRelicInternal(held, silent: true);
        }
    }

    private static void TestOfferShortages()
    {
        int[] draws = [55, 35, 72, 23, 69, 59, 38, 28, 41, 10, 36, 37, 19, 20, 29, 3,
            61, 47, 61, 23, 18, 82, 38, 19, 57, 26, 48, 62, 36];
        int[][] ancientAtFourToSix =
        [
            [0, 0, 0, 38, 9, 53], [0, 0, 0, 40, 10, 50], [0, 0, 0, 40, 12, 48]
        ];
        var results = draws.Select((roll, index) =>
            (RavenOutcome)RavenGameplayOddsHooks.Select(ancientAtFourToSix[Math.Min(2, index)], roll)).ToArray();
        Check(results.Count(result => result == RavenOutcome.Downgrade) == 17
            && results.Count(result => result == RavenOutcome.Keep) == 9 && results.Count(result => result == RavenOutcome.Bonus) == 3,
            "The reported Ancient sequence did not reproduce the original draws.");

        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 44101UL);
        var run = (RunState)CreatePoolFixtureRun(player);
        run.PushRoom(new TreasureRoom(0));
        run.AppendToMapPointHistory(MapPointType.Treasure, RoomType.Treasure, null);
        var stake = (RelicModel)ModelDb.Relic<RazorTooth>().ToMutable();
        var ancient = (RelicModel)ModelDb.Relic<WarHammer>().ToMutable();
        player.AddRelicInternal(stake, silent: true);
        player.AddRelicInternal(ancient, silent: true);

        var swap = (RavenOutcome)RavenGameplayOddsHooks.Select([18, 42, 26, 5, 9, 0], 68);
        Check(swap == RavenOutcome.Swap, "Razor Tooth roll 68 must be a swap.");
        var history = run.CurrentMapPointHistoryEntry!.PlayerStats.Single();
        var passives = RavenPoolHooks.CreateCandidates(player, RelicRarity.Rare, 1000, new Rng(918UL))
            .Where(relic => !RavenRelicHistoryHooks.HasPickupBenefit(relic)).ToArray();
        Check(passives.Length >= 3, "Rare passive fixture pool is incomplete.");
        history.RelicsRemoved.AddRange(passives.Select(relic => relic.Id));
        history.RelicsRemoved.Add(ModelDb.Relic<OldCoin>().Id);
        var down = RavenServiceHooks.CreateOffer(player, ancient, RavenOutcome.Downgrade, new Rng(919UL));
        Check(down is { Outcome: RavenOutcome.Downgrade, Shortage: null, Target: RelicRarity.Rare, Candidates.Count: 1 }
            && down.Candidates.All(saved => saved.Id != ModelDb.Relic<OldCoin>().Id),
            "An Ancient downgrade lost its available Rare or revived an immediate benefit.");

        var available = RavenPoolHooks.CreateCandidates(player, RelicRarity.Rare, 1000, new Rng(920UL));
        var blocked = RavenRewardReservationHooks.Track(new RewardsSet(player).WithCustomRewards(
            available.Skip(2).Select(relic => (Reward)CreateRelicReward(relic, player)).ToList()));
        try
        {
            var bonus = RavenServiceHooks.CreateOffer(player, stake, RavenOutcome.Bonus, new Rng(921UL));
            Check(bonus is { Outcome: RavenOutcome.Keep, Shortage: RavenOutcome.Bonus, Candidates.Count: 0 },
                "Two available choices must return the original without displaying a bonus.");
        }
        finally { blocked.Release(); }
        var allBlocked = RavenRewardReservationHooks.Track(new RewardsSet(player).WithCustomRewards(
            available.Select(relic => (Reward)CreateRelicReward(relic, player)).ToList()));
        try
        {
            var locked = RavenServiceHooks.CreateOffer(player, stake, swap, new Rng(922UL));
            Check(locked is { Outcome: RavenOutcome.Keep, Shortage: RavenOutcome.Swap, Candidates.Count: 0 }
                && locked.Notice == $"平换无可用遗物，{locked.StakeTitle}已返还，本局不可再次投入。", "Shortage has wrong result or wording.");
            var state = RavenStorageHooks.Get(player);
            RavenSessionHooks.Enqueue(state, 4, "fixture");
            Check(RavenTransactionHooks.CommitAsync(state, locked, () => throw new InvalidOperationException("Retained original removed."),
                () => { }).GetAwaiter().GetResult(), "Shortage did not commit.");
            var restored = JsonSerializer.Deserialize<RavenState>(JsonSerializer.Serialize(state, SaveOptions), SaveOptions)!;
            Check(restored.BlockedStakeIds.Contains(stake.Id.ToString()) && !RavenStakeEligibilityHooks.CanStake(player, stake)
                && RavenStakeEligibilityHooks.CanStake(player, ancient), "The shortage restriction was not scoped to the same ID.");
            Check(RavenStageHooks.PopText(RavenServiceHooks.BuildPlan(player, state)).Item1 == "原件返还", "Shortage did not use the retained-original pop.");
        }
        finally { allBlocked.Release(); }
        Check(!RunManager.Instance.IsInProgress, "Offer checks started a live run.");
    }

    private static void TestGameplayOddsConfig()
    {
        var packaged = RavenGameplayOddsHooks.ReadPublishedJson();
        var odds = RavenGameplayOddsConfigHooks.Parse(packaged);
        Check(odds.LightUpPercent == RavenGameplayOddsHooks.LightUpPercent
            && RavenRulesHooks.DescribeOdds(0).Contains($"{odds.LightUpPercent}%"), "Runtime and odds description did not use the packaged rate.");
        var rng = new Rng(731UL);
        var reference = new Rng(731UL);
        int initialDraw = reference.NextInt(0, odds.InitialLights.Sum());
        Check(RavenGameplayOddsHooks.InitialLights(rng) == RavenGameplayOddsHooks.Select(odds.InitialLights, initialDraw)
            && rng.ToSerializable().counter == 1, "Starting lights did not use one packaged-weight draw.");
        var customJson = System.Text.Json.Nodes.JsonNode.Parse(packaged)!.AsObject();
        customJson["lightUpPercent"] = 75;
        var custom = RavenGameplayOddsConfigHooks.Parse(customJson.ToJsonString());
        Check(custom.LightUpPercent == 75, "Editable chance did not parse.");
        try
        {
            customJson["lightUpPercent"] = 101;
            RavenGameplayOddsConfigHooks.Parse(customJson.ToJsonString());
            throw new InvalidOperationException("Invalid candle rate was accepted.");
        }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("lightUpPercent"), "Config validation missed the failing field.");
        }
        string path = Path.Combine(OS.GetUserDataDir(), "SurpriseRaven", "gameplay-odds.json");
        Check(!File.Exists(path), "Startup generated an AppData odds override.");
    }
}

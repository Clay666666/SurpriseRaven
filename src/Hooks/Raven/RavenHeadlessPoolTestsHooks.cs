using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestRelicHistoryAndPools()
    {
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 37521UL);
        var run = CreatePoolFixtureRun(player);
        run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, null);
        var entry = run.CurrentMapPointHistoryEntry!.PlayerStats.Single(p => p.PlayerId == player.NetId);
        var picked = ModelDb.Relic<Strawberry>();
        var bought = ModelDb.Relic<LetterOpener>();
        var removed = ModelDb.Relic<Pocketwatch>();
        var skipped = ModelDb.Relic<Anchor>();
        entry.RelicChoices.Add(new ModelChoiceHistoryEntry { choice = picked.Id, wasPicked = true });
        entry.RelicChoices.Add(new ModelChoiceHistoryEntry { choice = skipped.Id, wasPicked = false });
        entry.BoughtRelics.Add(bought.Id);
        entry.RelicsRemoved.Add(removed.Id);
        var obtained = RavenRelicHistoryHooks.GetObtainedRelicIds(player);
        Check(obtained.IsSupersetOf(new[] { picked.Id, bought.Id, removed.Id }) && !obtained.Contains(skipped.Id)
            && player.Relics.All(relic => obtained.Contains(relic.Id)), "Incorrect owned and native history union.");
        var copy = JsonSerializer.Deserialize<PlayerMapPointHistoryEntry>(JsonSerializer.Serialize(entry, SaveOptions),
            SaveOptions) ?? throw new InvalidOperationException("Missing restored history entry.");
        HashSet<ModelId> saved = [];
        RavenRelicHistoryHooks.IncludeObtained(saved, player.NetId, [copy, new()
        {
            PlayerId = player.NetId + 1, BoughtRelics = [skipped.Id]
        }]);
        Check(saved.SetEquals([picked.Id, bought.Id, removed.Id]), "Completed history must round trip per player.");
        Check(!RavenPoolHooks.CanClaim(player, picked.ToMutable()) && RavenPoolHooks.CanClaim(player, skipped.ToMutable()),
            "Picked and skipped candidate eligibility differs from history.");
        var allCommon = RavenPoolHooks.CreateCandidates(player, skipped.Rarity, 100, new Rng(172UL));
        Check(allCommon.Any(relic => relic.Id == skipped.Id) && allCommon.All(relic => !obtained.Contains(relic.Id)),
            "Catalog generation did not apply historical ownership or dropped a skipped candidate.");

        var neow = ModelDb.Event<Neow>().AllPossibleOptions.Select(option => option.Relic).OfType<RelicModel>()
            .DistinctBy(relic => relic.Id).ToArray();
        Check(neow.Length == 30 && neow.All(relic => relic.Rarity == RelicRarity.Ancient
            && RavenNeowRelicsHooks.Contains(relic.Id) && !RavenStakeEligibilityHooks.CanStake(relic)),
            "Every Neow identity must be banned as a stake.");
        Check(RavenStakeEligibilityHooks.CanStake(picked.ToMutable()), "A derived normal relic lost stake eligibility.");
        Check(RavenPoolHooks.CanClaim(player, ModelDb.Relic<GoldenPearl>().ToMutable()),
            "The new draw restriction should not erase a valid locked legacy candidate.");
        var sources = RavenPoolHooks.UnlockedAncients(player).ToArray();
        Check(sources.Length > 1 && sources.All(source => source is not Neow), "Neow was kept as an Ancient source.");
        var ancientPool = RavenPoolHooks.CreateCandidates(player, RelicRarity.Ancient, 100, new Rng(287UL));
        Check(ancientPool.Count > 3 && ancientPool.All(relic => !RavenNeowRelicsHooks.Contains(relic.Id)),
            "Ancient upgrades should exclude the complete Neow pool.");

        var sample = RavenPoolHooks.CreateAncientBonusCandidates(player, new Rng(244UL));
        var replay = RavenPoolHooks.CreateAncientBonusCandidates(player, new Rng(244UL));
        var group = sources.Single(source => source.Id.ToString() == sample.AncientId);
        var groupIds = RavenPoolHooks.AncientRelics(group).Select(relic => relic.Id).ToHashSet();
        Check(sample.Candidates.Count is >= 1 and <= 3 && sample.Candidates.All(relic => groupIds.Contains(relic.Id))
            && sample.Candidates.DistinctBy(relic => relic.Id).Count() == sample.Candidates.Count,
            "Ancient bonus mixed sources or repeated a relic.");
        Check(replay.AncientId == sample.AncientId && sample.Candidates.Select(relic => relic.Id)
            .SequenceEqual(replay.Candidates.Select(relic => relic.Id)), "Seeded selection did not replay.");
        var keep = sample.Candidates[0].Id;
        entry.RelicsRemoved.AddRange(sources.SelectMany(RavenPoolHooks.AncientRelics)
            .Where(relic => relic.Id != keep).Select(relic => relic.Id).Distinct());
        var one = RavenPoolHooks.CreateAncientBonusCandidates(player, new Rng(245UL));
        Check(one.Candidates.Select(relic => relic.Id).SequenceEqual([keep]), "A short group should give its last eligible relic.");
        entry.RelicsRemoved.Add(keep);
        var empty = RavenPoolHooks.CreateAncientBonusCandidates(player, new Rng(245UL));
        Check(empty.AncientId is null && empty.Candidates.Count == 0, "An exhausted pool regenerated an obtained relic.");
        Check(!RunManager.Instance.IsInProgress, "History fixture started a live run.");
    }

    private static IRunState CreatePoolFixtureRun(Player player)
    {
        var factory = typeof(RunState).GetMethod("CreateForTest", BindingFlags.Public | BindingFlags.Static)!;
        var args = factory.GetParameters().Select(parameter => parameter.Name switch
        {
            "players" => (object)new[] { player },
            "seed" => "RAVENPOOL033",
            _ => parameter.DefaultValue
        }).ToArray();
        return (IRunState)factory.Invoke(null, args)!;
    }
}

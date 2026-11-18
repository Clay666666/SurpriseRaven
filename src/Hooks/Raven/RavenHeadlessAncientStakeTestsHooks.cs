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
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestAncientStakeEligibility()
    {
        // Compare the whole native reward catalog against the approved list.
        const string expected = """
            PaelsHorn Storybook SereTalon DistinguishedCape GlassEye SeaGlass JewelryBox TanxsWhistle DustyTome
            PaelsClaw PaelsGrowth NutritiousSoup YummyCookie ElectricShrymp SandCastle BeautifulBracelet TriBoomerang
            PaelsTooth BiiigHug PreservedFog ArchaicTooth Claws Astrolabe EmptyCage PandorasBox
            ToyBox CallingBell AlchemicalCoffer LoomingFruit SignetRing
            GoldenCompass TouchOfOrobas
            """;
        var names = expected.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        var catalog = ModelDb.AllAncients.Where(ancient => ancient is not Neow)
            .SelectMany(RavenPoolHooks.AncientRelics).DistinctBy(relic => relic.Id).ToArray();
        var blocked = catalog.Where(relic => !RavenStakeEligibilityHooks.CanStake(relic)).ToArray();
        Check(names.Count == 32 && names.SetEquals(blocked.Select(relic => relic.GetType().Name)),
            "The native non-Neow Ancient stake restrictions differ from the approved list.");

        var beforePlayer = RavenServiceHooks.CurrentPlayer;
        bool beforeBusy = RavenServiceHooks.Busy;
        foreach (var canonical in blocked)
        {
            var relic = canonical.ToMutable();
            Check(relic.Rarity == RelicRarity.Ancient && !RavenNeowRelicsHooks.Contains(relic.Id)
                && !RavenRulesHooks.IsExcluded(relic) && RavenRulesHooks.GrantsOpportunity(relic.Rarity),
                $"Stake restriction leaked into reward or opportunity eligibility: {relic.Id}.");
            Check(!RavenStakeEligibilityHooks.CanStake(relic)
                && RavenServiceHooks.SubmitStakeAsync(null!, relic).GetAwaiter().GetResult() is null,
                $"Pickup-effect Ancient was accepted as a stake: {relic.Id}.");

            var json = JsonSerializer.Serialize(relic.ToSerializable(), SaveOptions);
            var saved = JsonSerializer.Deserialize<SerializableRelic>(json, SaveOptions)
                ?? throw new InvalidOperationException($"Ancient stake fixture did not restore: {relic.Id}.");
            var restored = RelicModel.FromSerializable(saved);
            Check(restored.Id == relic.Id && !ReferenceEquals(restored, relic)
                && !RavenStakeEligibilityHooks.CanStake(restored)
                && RavenServiceHooks.SubmitStakeAsync(null!, restored).GetAwaiter().GetResult() is null,
                $"Restored pickup-effect Ancient was accepted as a stake: {relic.Id}.");
        }
        Check(ReferenceEquals(beforePlayer, RavenServiceHooks.CurrentPlayer) && beforeBusy == RavenServiceHooks.Busy,
            "Rejected Ancient stakes changed the live service.");

        RelicModel[] allowed = [ModelDb.Relic<LordsParasol>(), ModelDb.Relic<WarHammer>(), ModelDb.Relic<MeatCleaver>()];
        foreach (var relic in allowed)
            Check(RavenStakeEligibilityHooks.CanStake(relic.ToMutable()),
                $"Approved delayed-effect Ancient was blocked: {relic.Id}.");

        // Use a detached reward player. Neither award generation nor CanClaim grants a relic.
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 918275UL);
        var run = CreatePoolFixtureRun(player);
        var target = ModelDb.Relic<GlassEye>().Id;
        var upgrade = RavenPoolHooks.CreateCandidates(player, RelicRarity.Ancient, catalog.Length, new Rng(920UL))
            .SingleOrDefault(relic => relic.Id == target);
        Check(upgrade is not null && !RavenStakeEligibilityHooks.CanStake(upgrade)
            && RavenPoolHooks.CanClaim(player, upgrade), "Glass Eye stopped being a claimable Ancient upgrade.");

        run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, null);
        var history = run.CurrentMapPointHistoryEntry!.PlayerStats.Single(p => p.PlayerId == player.NetId);
        history.RelicsRemoved.AddRange(catalog.Select(relic => relic.Id).Where(id => id != target));
        var bonus = RavenPoolHooks.CreateAncientBonusCandidates(player, new Rng(921UL)).Candidates;
        Check(bonus is [var remaining] && remaining.Id == target && RavenPoolHooks.CanClaim(player, remaining),
            "The last available Ancient bonus was excluded because it cannot be staked.");
        Check(!RunManager.Instance.IsInProgress && player.Relics.All(relic => relic.Id != target),
            "The reward fixture changed inventory or started a live run.");
    }
}

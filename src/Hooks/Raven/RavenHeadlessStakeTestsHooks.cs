using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestStakeEligibility()
    {
        (RelicModel Starter, RelicModel Upgrade)[] pairs =
        [
            (ModelDb.Relic<BurningBlood>(), ModelDb.Relic<BlackBlood>()),
            (ModelDb.Relic<RingOfTheSnake>(), ModelDb.Relic<RingOfTheDrake>()),
            (ModelDb.Relic<DivineRight>(), ModelDb.Relic<DivineDestiny>()),
            (ModelDb.Relic<BoundPhylactery>(), ModelDb.Relic<PhylacteryUnbound>()),
            (ModelDb.Relic<CrackedCore>(), ModelDb.Relic<InfusedCore>())
        ];
        var refinement = ModelDb.Relic<TouchOfOrobas>();
        var beforePlayer = RavenServiceHooks.CurrentPlayer;
        bool beforeBusy = RavenServiceHooks.Busy;
        foreach (var (starter, upgrade) in pairs)
        {
            Check(refinement.GetUpgradedStarterRelic(starter).Id == upgrade.Id,
                $"Local starter mapping changed: {starter.Id}.");
            foreach (var canonical in new[] { starter, upgrade })
            {
                var relic = canonical.ToMutable();
                Check(RavenStakeEligibilityHooks.IsStarterOrUpgrade(relic)
                    && !RavenStakeEligibilityHooks.CanStake(relic), $"Starter identity accepted: {relic.Id}.");
                // A rejected stake must return before any player, run data or RNG access.
                Check(RavenServiceHooks.SubmitStakeAsync(null!, relic).GetAwaiter().GetResult() is null, $"Starter stake submitted: {relic.Id}.");
                var json = JsonSerializer.Serialize(relic.ToSerializable(), SaveOptions);
                var saved = JsonSerializer.Deserialize<SerializableRelic>(json, SaveOptions)
                    ?? throw new InvalidOperationException("Starter save did not restore.");
                var restored = RelicModel.FromSerializable(saved);
                Check(restored.Id == relic.Id && !ReferenceEquals(restored, relic)
                    && !RavenStakeEligibilityHooks.CanStake(restored), $"Restored starter accepted: {relic.Id}.");
                Check(RavenServiceHooks.SubmitStakeAsync(null!, restored).GetAwaiter().GetResult() is null, $"Restored starter submitted: {relic.Id}.");
            }
        }
        Check(ReferenceEquals(beforePlayer, RavenServiceHooks.CurrentPlayer)
            && beforeBusy == RavenServiceHooks.Busy, "Rejected submission changed the live service.");
        foreach (var starter in ModelDb.AllRelics.Where(r => r.Rarity == RelicRarity.Starter))
            Check(!RavenStakeEligibilityHooks.CanStake(starter), $"Starter rarity accepted: {starter.Id}.");
        foreach (var relic in new RelicModel[] { ModelDb.Relic<Circlet>(), ModelDb.Relic<LordsParasol>() })
            Check(!RavenStakeEligibilityHooks.IsStarterOrUpgrade(relic)
                && RavenStakeEligibilityHooks.CanStake(relic), $"Unrelated relic was blocked: {relic.Id}.");
        Check(refinement.GetUpgradedStarterRelic(ModelDb.Relic<GoldenPearl>()).Id == ModelDb.Relic<Circlet>().Id,
            "Unknown refinement fixture no longer returns Circlet.");
        foreach (var pair in pairs)
            Check(!RavenRulesHooks.IsExcluded(pair.Upgrade), "Stake restriction leaked into global reward exclusion.");

        // Only constructs a detached model; never starts a run or invokes pickup effects.
        // The factory may mark starting inventory as seen, confined to the isolated test save.
        Check(!RunManager.Instance.IsInProgress, "Stake fixture must have no active run.");
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.none, 918273UL);
        foreach (var pair in pairs) player.AddRelicInternal(pair.Upgrade.ToMutable(), silent: true);
        var normal = ModelDb.Relic<Strawberry>().ToMutable();
        var ancient = ModelDb.Relic<GoldenPearl>().ToMutable();
        var circlet = ModelDb.Relic<Circlet>().ToMutable();
        var removed = ModelDb.Relic<Strawberry>().ToMutable();
        // ILSpy verified a private setter; model fixture only, without removal effects.
        typeof(RelicModel).GetProperty(nameof(RelicModel.HasBeenRemovedFromState))!.SetValue(removed, true);
        var eventRelic = ModelDb.AllRelics.First(r => r.Rarity == RelicRarity.Event).ToMutable();
        foreach (var relic in new[] { normal, ancient, circlet, removed, eventRelic })
            player.AddRelicInternal(relic, silent: true);
        Check(RavenServiceHooks.GetStakes(player).SequenceEqual(new[] { normal, circlet }),
            "Stake list did not apply the shared restriction to inventory instances.");
        Check(!RunManager.Instance.IsInProgress, "Detached stake fixture unexpectedly started a run.");
        TestAncientStakeEligibility();
    }
}

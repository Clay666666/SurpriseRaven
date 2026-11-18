using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    // Locked results must survive saves unchanged, never leak across opportunities, and migrate 0.2.x saves.
    private static void TestPendingRounds()
    {
        var state = new RavenState();
        RavenSessionHooks.Enqueue(state, 2, "room-a");
        Check(state.OpportunityId == 1 && state.Pending is null, "New opportunity identity differs.");
        var glass = (SeaGlass)ModelDb.Relic<SeaGlass>().ToMutable();
        glass.CharacterId = ModelDb.Character<Ironclad>().Id;
        state.Pending = new RavenPendingRound
        {
            Outcome = RavenOutcome.Downgrade, StakeId = "RELIC.FIXTURE", StakeIndex = 3, StakeTitle = "测试",
            Target = RelicRarity.Uncommon, NextLights = 4, Candidates = [glass.ToSerializable()]
        };
        RavenSessionHooks.Enqueue(state, 5, "room-a");
        Check(state.OpportunityId == 1 && state.Pending is not null, "Queued offer disturbed the locked round.");
        var copy = JsonSerializer.Deserialize<RavenState>(JsonSerializer.Serialize(state, SaveOptions), SaveOptions)
                   ?? throw new InvalidOperationException("State did not restore.");
        var restored = copy.Pending ?? throw new InvalidOperationException("Locked round lost during save.");
        Check(restored.Outcome == RavenOutcome.Downgrade && restored.StakeId == "RELIC.FIXTURE" && restored.StakeIndex == 3
              && restored.Target == RelicRarity.Uncommon && restored.NextLights == 4 && restored.StakeTitle == "测试"
              && !restored.RoundAlreadyApplied,
            "Locked round fields changed during save.");
        Check(restored.Candidates.Count == 1 && RelicModel.FromSerializable(restored.Candidates[0]) is SeaGlass,
            "Locked round candidate changed during save.");
        Check(copy.OpportunityId == 1, "Opportunity identity lost during save.");

        RavenSessionHooks.CompletePendingRound(state, state.Pending!);
        Check(state.Lights == 4 && state.Round == 1 && state.IsActive, "Applied pending round differs.");
        RavenSessionHooks.CompletePendingRound(state, state.Pending!);
        Check(state.Lights == 4 && state.Round == 1, "A completed pending round advanced twice.");
        Reject(() => RavenSessionHooks.ApplyRound(state, 7), "Seven lights accepted.");
        RavenSessionHooks.EndOpportunity(state, "退出");
        Check(state.Pending is null && state.OpportunityId == 2 && state.Lights == 5 && state.Round == 0,
            "Ending an offer leaked its locked result or identity.");
        state.Pending = new RavenPendingRound { Outcome = RavenOutcome.Upgrade };
        RavenSessionHooks.Expire(state, "离开房间");
        Check(state.Pending is null && !state.HasOpportunity, "Room expiry kept a locked result.");

        var legacy = new RavenState { BonusCandidates = [glass.ToSerializable(), glass.ToSerializable()] };
        long revision = legacy.Revision;
        Check(RavenSessionHooks.MigrateLegacy(legacy), "Legacy bonus choice was not migrated.");
        Check(legacy.Pending is { Outcome: RavenOutcome.Bonus, NextLights: -1, RoundAlreadyApplied: true } migrated && migrated.Candidates.Count == 2
              && legacy.BonusCandidates.Count == 0 && legacy.Revision == revision + 1, "Legacy migration differs.");
        Check(!RavenSessionHooks.MigrateLegacy(legacy), "Legacy migration repeated.");
        Check(!RavenSessionHooks.MigrateLegacy(new RavenState()), "Empty state migrated.");
        TestPendingRoundMigrationCompletion(glass.ToSerializable());

        foreach (var outcome in Enum.GetValues<RavenOutcome>())
            Check(RavenServiceHooks.IsReplacement(outcome) == outcome is RavenOutcome.Upgrade or RavenOutcome.Swap or RavenOutcome.Downgrade,
                $"Replacement classification differs for {outcome}.");
        Check(!RavenStageSession.TimelineActive, "Headless fixture left a round animation active.");
    }

    private static void TestLegacyCommonDowngrades()
    {
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.none, 22344UL);
        CreatePoolFixtureRun(player);
        foreach (var (stake, reward) in new (RelicModel, RelicModel)[]
        {
            (ModelDb.Relic<RegalPillow>(), ModelDb.Relic<JuzuBracelet>()),
            (ModelDb.Relic<RedMask>(), ModelDb.Relic<BoneFlute>())
        })
        {
            var state = new RavenState { PendingLights = [2], Lights = 2, Pending = new RavenPendingRound
            {
                Outcome = RavenOutcome.Downgrade, Target = RelicRarity.Common,
                SettlementVersion = RavenTransactionHooks.CurrentVersion, StakeConsumed = true,
                StakeSnapshot = stake.ToMutable().ToSerializable(), StakeId = stake.Id.ToString(),
                NextLights = 3, Candidates = [reward.ToMutable().ToSerializable()]
            } };
            var restored = JsonSerializer.Deserialize<RavenState>(JsonSerializer.Serialize(state, SaveOptions), SaveOptions)!;
            var pending = restored.Pending!;
            var plan = RavenServiceHooks.BuildPlan(player, restored);
            Check(stake.Rarity == RelicRarity.Common && reward.Rarity == RelicRarity.Common
                && plan.DisplayOutcome == RavenOutcome.Swap && RavenStageHooks.PopText(plan).Item1 == "等价互换",
                $"Legacy {stake.Id} to {reward.Id} should show an equal trade.");
            Check(pending.Outcome == RavenOutcome.Downgrade && pending.StakeConsumed
                && plan.Rewards.Single().Id == reward.Id && plan.LightsAfter == 3, "Legacy reward changed during restore.");
            int claims = 0;
            var result = RavenTransactionHooks.CollectAsync(restored, pending, 0, _ => true, index =>
            {
                Check(RelicModel.FromSerializable(pending.Candidates[index]).Id == reward.Id, "Claim changed the locked candidate.");
                claims++;
                return Task.CompletedTask;
            }, () => { }).GetAwaiter().GetResult();
            Check(result == RavenClaimResult.Claimed && claims == 1 && restored.Pending is null && restored.Lights == 3,
                "Legacy Common trade failed to settle the original candidate once.");
        }
    }
}

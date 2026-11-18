using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestNativeAncients()
    {
        Check(DisplayServer.GetName() == "headless" && !RunManager.Instance.IsInProgress, "Native fixtures must be isolated.");
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 37033UL);
        var run = CreatePoolFixtureRun(player);
        run.CurrentActIndex = 1;
        var originalRelics = player.Relics.ToArray();
        var originalCards = player.Deck.Cards.ToArray();
        foreach (var type in RavenNativeAncientOptionsHooks.NativeTypes)
        {
            var canonical = ModelDb.AllAncients.Single(a => a.GetType() == type);
            var model = AncientFixture(canonical, player);
            var rewardsBefore = player.PlayerRng.Rewards.ToSerializable();
            var clean = RavenNativeAncientPoolsHooks.Build(model, player, new HashSet<ModelId>());
            Check(rewardsBefore == player.PlayerRng.Rewards.ToSerializable(), $"Preflight consumed Rewards RNG: {canonical.Id}.");
            Check(clean.Viable, $"Detached starting-deck native pool cannot offer choices: {canonical.Id}.");
            var skipped = clean.Rows[0].First(o => o.Relic is not null).Relic!.Id;
            HashSet<ModelId> obtained = [skipped];
            using (RavenNativeAncientOptionsHooks.Fixture(player, obtained))
            {
                var result = InvokeNativeAncientOptions(model);
                var group = RavenPoolHooks.AncientRelics(canonical).Select(r => r.Id).ToHashSet();
                Check(result.Count == 3 && result.All(o => o.Relic is null || (!obtained.Contains(o.Relic.Id) && group.Contains(o.Relic.Id))),
                    $"Native wrapper mixed sources, repeated obtained ID, or lost slots: {canonical.Id}.");
                var callback = typeof(EventOption).GetProperty("OnChosen", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Check(result.Where(o => o.Relic is not null).All(o => callback.GetValue(o) is not null),
                    $"Native pickup callbacks were discarded: {canonical.Id}.");
                var eventRng = model.Rng.ToSerializable();
                var rewardsRng = player.PlayerRng.Rewards.ToSerializable();
                var again = InvokeNativeAncientOptions(model);
                Check(result.SequenceEqual(again) && eventRng == model.Rng.ToSerializable()
                    && rewardsRng == player.PlayerRng.Rewards.ToSerializable(), $"Repeated wrapper rerolled or reinitialized {canonical.Id}.");
            }
            var fullHistory = RavenPoolHooks.AncientRelics(canonical).Select(r => r.Id).ToHashSet();
            Check(!RavenNativeAncientPoolsHooks.Build(AncientFixture(canonical, player), player, fullHistory).Viable,
                $"Fully obtained NPC was not exhausted: {canonical.Id}.");
        }
        TestNativeAncientWeights(player);
        TestNativeAncientSelection(player);
        Check(player.Relics.SequenceEqual(originalRelics) && player.Deck.Cards.SequenceEqual(originalCards)
            && !RunManager.Instance.IsInProgress, "Native option tests modified inventory/cards or began a run.");
    }

    private static AncientEventModel AncientFixture(AncientEventModel canonical, Player player)
    {
        var model = (AncientEventModel)canonical.ToMutable();
        typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(model, player);
        typeof(EventModel).GetProperty(nameof(EventModel.Rng))!.SetValue(model, new Rng(7033UL));
        return model;
    }

    private static IReadOnlyList<EventOption> InvokeNativeAncientOptions(AncientEventModel model) =>
        (IReadOnlyList<EventOption>)AccessTools.DeclaredMethod(typeof(AncientEventModel), "GenerateInitialOptionsWrapper").Invoke(model, null)!;

    private static void TestNativeAncientWeights(Player player)
    {
        var pael = RavenNativeAncientPoolsHooks.Build(AncientFixture(ModelDb.AncientEvent<Pael>(), player), player, new HashSet<ModelId>());
        var middle = pael.Rows[1];
        Check(middle.Count(o => o.Relic is PaelsGrowth) == 1 && middle.Count(o => o.Relic is PaelsWing) == 2,
            "Pael lost its weighted middle pool.");
        var orobas = RavenNativeAncientPoolsHooks.Build(AncientFixture(ModelDb.AncientEvent<Orobas>(), player), player, new HashSet<ModelId>());
        Check(orobas.Rows[0].Count(o => o.Relic is ElectricShrymp) == 3
            && orobas.Rows[0].Count(o => o.Relic is GlassEye) == 3
            && orobas.Rows[0].Count(o => o.Relic is PrismaticGem) == 1
            && orobas.Rows[0].Count(o => o.Relic is SeaGlass) == 2, "Orobas marginal weights changed.");
        var vakuu = AncientFixture(ModelDb.AncientEvent<Vakuu>(), player);
        HashSet<ModelId> denied = [ModelDb.Relic<PreservedFog>().Id, ModelDb.Relic<DistinguishedCape>().Id];
        using (RavenNativeAncientOptionsHooks.Fixture(player, denied))
        {
            var chosen = InvokeNativeAncientOptions(vakuu).Single(o => o.Relic is SereTalon);
            Check(chosen.WillKillPlayer is not null,
                "SereTalon lost its native maximum-HP loss warning predicate.");
        }
        var tome = AncientFixture(ModelDb.AncientEvent<Darv>(), player);
        var keep = new[] { ModelDb.Relic<Astrolabe>().Id, ModelDb.Relic<BlackStar>().Id, ModelDb.Relic<DustyTome>().Id };
        var tomeDenied = RavenPoolHooks.AncientRelics(ModelDb.AncientEvent<Darv>()).Where(r => !keep.Contains(r.Id)).Select(r => r.Id).ToHashSet();
        using (RavenNativeAncientOptionsHooks.Fixture(player, tomeDenied))
        {
            var before = player.PlayerRng.Rewards.ToSerializable().counter;
            var first = InvokeNativeAncientOptions(tome);
            var after = player.PlayerRng.Rewards.ToSerializable().counter;
            Check(first.Single(o => o.Relic is DustyTome).Relic is DustyTome { AncientCard: not null } && after == before + 1,
                "Selected DustyTome must initialize from native Rewards RNG once.");
            InvokeNativeAncientOptions(tome);
            Check(player.PlayerRng.Rewards.ToSerializable().counter == after, "Rebuilding native options initialized DustyTome twice.");
        }
        var exhausted = AncientFixture(ModelDb.AncientEvent<Tanx>(), player);
        using (RavenNativeAncientOptionsHooks.Fixture(player, new HashSet<ModelId>(), exhausted: true))
        {
            var options = InvokeNativeAncientOptions(exhausted);
            Check(options.Count == 1 && options[0].IsProceed && options[0].Relic is null,
                "Exhausted chapter must expose the native no-reward Continue option.");
        }
    }
}

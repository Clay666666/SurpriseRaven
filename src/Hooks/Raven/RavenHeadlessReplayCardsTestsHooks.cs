using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    // The relics generate their real choices. At the presentation boundary the fixture captures the
    // offered cards and returns an empty selection, without entering a run, UI or completion command.
    private static List<string>? _capturedPreview;
    private static void TestNativeRavenPreviewCards()
    {
        Check(DisplayServer.GetName() == "headless" && !RunManager.Instance.IsInProgress, "Native preview probe needs isolated headless.");
        var harmony = new Harmony("SurpriseRaven.HeadlessPreview");
        var offer = AccessTools.DeclaredMethod(typeof(RewardsCmd), nameof(RewardsCmd.OfferCustom));
        var grid = AccessTools.DeclaredMethod(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGridForRewards));
        var captureRewards = AccessTools.DeclaredMethod(typeof(RavenHeadlessTestsHooks), nameof(CaptureGlassEyeRewards));
        var captureGrid = AccessTools.DeclaredMethod(typeof(RavenHeadlessTestsHooks), nameof(CaptureSeaGlassGrid));
        try
        {
            harmony.Patch(offer, prefix: new HarmonyMethod(captureRewards));
            harmony.Patch(grid, prefix: new HarmonyMethod(captureGrid));
            var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 4454UL);
            var run = (RunState)CreatePoolFixtureRun(player);
            player.PlayerRng.Rewards.NextUnsignedLong();
            player.PlayerRng.Rewards.NextUnsignedLong();
            var doc = CreateReplayRunDocument(player, run);
            var before = RavenReplayProjectionHooks.Encode(doc);
            var sea = (SeaGlass)ModelDb.Relic<SeaGlass>().ToMutable();
            sea.CharacterId = ModelDb.Character<Silent>().Id;
            foreach (var candidate in new RelicModel[] { ModelDb.Relic<GlassEye>().ToMutable(), sea })
            {
                (string Cards, ulong Next) Draw()
                {
                    var restored = RunState.FromSerializable(RavenReplayProjectionHooks.Decode(before)).Players.Single();
                    int count = restored.Deck.Cards.Count;
                    var reward = RelicModel.FromSerializable(candidate.ToSerializable());
                    reward.Owner = restored;
                    _capturedPreview = [];
                    var choice = reward.AfterObtained();
                    Check(choice.IsCompleted, "Native preview unexpectedly waited for a real screen.");
                    choice.GetAwaiter().GetResult();
                    Check(_capturedPreview.Count == (candidate is GlassEye ? 5 : 1)
                        && _capturedPreview.Sum(group => group.Split(',').Length) == 15,
                        "The relic did not expose all of its real card choices.");
                    Check(restored.Deck.Cards.Count == count, "Merely previewing added a card.");
                    return (string.Join("|", _capturedPreview), restored.PlayerRng.Rewards.NextUnsignedLong());
                }
                var first = Draw();
                Check(first == Draw() && first == Draw(), $"Two independent reloads changed {candidate.Id}'s cards or subsequent native draw.");
            }
        }
        finally
        {
            _capturedPreview = null;
            harmony.Unpatch(offer, captureRewards);
            harmony.Unpatch(grid, captureGrid);
        }
    }

    private static string CardsOffered(IEnumerable<CardModel> cards) =>
        string.Join(",", cards.Select(card => $"{card.Id}+{card.CurrentUpgradeLevel}"));

    private static bool CaptureGlassEyeRewards(List<Reward> rewards, ref Task __result)
    {
        if (_capturedPreview is null) return true;
        foreach (var reward in rewards.Cast<CardReward>())
        {
            reward.Populate();
            _capturedPreview.Add(CardsOffered(reward.Cards));
        }
        __result = Task.CompletedTask;
        return false;
    }

    private static bool CaptureSeaGlassGrid(List<CardCreationResult> cards, ref Task<IEnumerable<CardModel>> __result)
    {
        if (_capturedPreview is null) return true;
        _capturedPreview.Add(CardsOffered(cards.Select(option => option.Card)));
        __result = Task.FromResult<IEnumerable<CardModel>>([]);
        return false;
    }
}

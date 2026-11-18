using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestNativeAncientSelection(Player player)
    {
        var acts = player.RunState.Acts;
        foreach (var act in acts)
            RavenNativeAncientSelectionHooks.SetAncient(act, act.GetUnlockedAncients(player.UnlockState).First());
        var second = acts[1];
        var third = acts[2];
        RavenNativeAncientSelectionHooks.SetAncient(second, ModelDb.AncientEvent<Pael>());
        RavenNativeAncientSelectionHooks.SetAncient(third, ModelDb.AncientEvent<Vakuu>());
        second.SetSharedAncientSubset([ModelDb.AncientEvent<Darv>()]);
        third.SetSharedAncientSubset([]);
        var data = new RavenNativeAncientState();
        RavenNativeAncientStorageHooks.SaveAllocations(player, data);
        var copy = JsonSerializer.Deserialize<RavenNativeAncientState>(JsonSerializer.Serialize(data, SaveOptions), SaveOptions)!;
        var chapterTwo = RavenNativeAncientStorageHooks.Candidates(player, second, copy);
        var chapterThree = RavenNativeAncientStorageHooks.Candidates(player, third, copy);
        Check(copy.AllocationsCaptured && chapterTwo.Any(a => a is Darv) && chapterThree.All(a => a is not Darv),
            "Saved shared Ancient allocation leaked Darv into the other chapter.");
        Check(chapterTwo.All(a => a is Orobas or Pael or Tezcatara or Darv)
            && chapterThree.All(a => a is Nonupeipe or Tanx or Vakuu), "Replacement candidates crossed native act boundaries.");
        var original = ModelDb.AncientEvent<Pael>();
        HashSet<ModelId> partial = [ModelDb.Relic<PaelsFlesh>().Id];
        int randomCalls = 0;
        var unchanged = RavenNativeAncientSelectionHooks.Choose(original, chapterTwo,
            a => RavenNativeAncientSelectionHooks.IsViable(a, player, partial), n => { randomCalls++; return 0; });
        Check(ReferenceEquals(unchanged, original) && randomCalls == 0, "A healthy original NPC was rerolled.");
        HashSet<ModelId> firstRow = [ModelDb.Relic<PaelsFlesh>().Id, ModelDb.Relic<PaelsHorn>().Id, ModelDb.Relic<PaelsTears>().Id];
        Check(!RavenNativeAncientSelectionHooks.IsViable(original, player, firstRow),
            "A fully obtained mandatory native row did not exhaust its NPC.");
        int probes = 0;
        var alternative = RavenNativeAncientSelectionHooks.Choose(original, chapterTwo, a =>
        {
            probes++;
            return RavenNativeAncientSelectionHooks.IsViable(a, player, firstRow);
        }, n => { randomCalls++; return n - 1; });
        Check(alternative is not null && alternative.Id != original.Id && chapterTwo.Contains(alternative)
            && probes <= chapterTwo.Count + 1 && randomCalls == 1, "Replacement search was not bounded to legal viable NPCs.");
        RavenNativeAncientSelectionHooks.SetAncient(second, alternative!);
        Check(second.PullAncient().Id == alternative!.Id && second.ToSave().SerializableRooms.AncientId == alternative.Id,
            "Replacement identity did not reach native PullAncient and serialized room identity.");
        var exhausted = chapterTwo.SelectMany(RavenPoolHooks.AncientRelics).Select(r => r.Id).ToHashSet();
        var none = RavenNativeAncientSelectionHooks.Choose(original, chapterTwo,
            a => RavenNativeAncientSelectionHooks.IsViable(a, player, exhausted), _ => throw new InvalidOperationException("Empty search consumed RNG."));
        Check(none is null, "Fully exhausted chapter generated another NPC's obtained reward.");
        string key = RavenNativeAncientStorageHooks.ActKey(player, second);
        data.Visits[key] = new RavenNativeAncientVisit { AncientId = original.Id.ToString(), Exhausted = true };
        copy = JsonSerializer.Deserialize<RavenNativeAncientState>(JsonSerializer.Serialize(data, SaveOptions), SaveOptions)!;
        Check(copy.Visits[key].Exhausted && copy.Visits[key].AncientId == original.Id.ToString(),
            "Exhausted visit identity was lost on restore.");

        RavenNativeAncientSelectionHooks.SetAncient(third, ModelDb.AncientEvent<Darv>());
        Check(RavenNativeAncientStorageHooks.Candidates(player, second, copy).All(a => a is not Darv),
            "A shared Ancient already selected in another act was reused.");
    }
}

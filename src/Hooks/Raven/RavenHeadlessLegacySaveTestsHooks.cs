using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Unlocks;
using STS2RitsuLib.RunData;
using STS2RitsuLib.RunRngs;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestLegacyPersistenceIdentity()
    {
        // Use fresh in-memory runs for RitsuLib's real export/import. No game profile is opened.
        const string legacyId = "Sts2ModScaffold";
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.none, 430UL);
        var run = CreatePoolFixtureRun(player);
        var reward = ModelDb.Relic<Strawberry>().ToMutable();
        var state = RavenStorageHooks.Get(player);
        RavenSessionHooks.Enqueue(state, 4, "fixture-room");
        state.Pending = new RavenPendingRound
        {
            Outcome = RavenOutcome.Swap, StakeConsumed = true,
            SettlementVersion = RavenTransactionHooks.CurrentVersion, Candidates = [reward.ToSerializable()]
        };
        RavenStorageHooks.Touch(player);
        var ancients = RavenNativeAncientStorageHooks.Get(player);
        ancients.Visits["fixture-act"] = new RavenNativeAncientVisit { AncientId = "fixture-ancient", Exhausted = true };
        RavenNativeAncientStorageHooks.Touch(player);

        string[] names = ["initial", "outcome", "reward", "lights", "nativeAncient"];
        var legacyStreams = names.Select(name => ModRunRngRegistry.Get(player, legacyId, "raven." + name)).ToArray();
        for (int i = 0; i < legacyStreams.Length; i++)
            for (int n = 0; n <= i; n++) legacyStreams[i].NextUnsignedLong();

        // RitsuLib 0.6.14 exposes its normal save serializer through this internal registry.
        var registry = typeof(RunSavedDataStore).Assembly.GetType("STS2RitsuLib.RunData.RunSavedDataRegistry", throwOnError: true)!;
        var export = registry.GetMethod("BuildPayload") ?? throw new MissingMethodException(registry.FullName, "BuildPayload");
        var import = registry.GetMethod("ImportPayloadIntoRun") ?? throw new MissingMethodException(registry.FullName, "ImportPayloadIntoRun");
        string payload = (string?)export.Invoke(null, [run]) ?? throw new InvalidOperationException("RitsuLib exported an empty fixture.");
        using var json = JsonDocument.Parse(payload);
        var slots = json.RootElement.GetProperty("_ritsulib").GetProperty("run_saved_data").GetProperty(legacyId);
        Check(slots.TryGetProperty("raven_lamp", out _) && slots.TryGetProperty("raven_native_ancients", out _),
            "The lamp and Ancient slots must be serialized under their original 0.4.2 keys.");

        var restored = Player.CreateForNewRun<Ironclad>(UnlockState.none, player.NetId);
        var restoredRun = CreatePoolFixtureRun(restored);
        import.Invoke(null, [restoredRun, payload]);
        var lamp = RavenStorageHooks.Get(restored);
        Check(lamp.Lights == 4 && lamp.PendingLights.SequenceEqual([4]) && lamp.RoomKey == "fixture-room",
            "The restored player lost the 0.4.2 lamp opportunity.");
        Check(lamp.Pending is { StakeConsumed: true, Outcome: RavenOutcome.Swap, Candidates.Count: 1 } pending
            && RelicModel.FromSerializable(pending.Candidates[0]).Id == reward.Id, "The restored player lost the pending reward.");
        Check(RavenNativeAncientStorageHooks.Get(restored).Visits.TryGetValue("fixture-act", out var visit)
            && visit is { AncientId: "fixture-ancient", Exhausted: true }, "The restored player lost the Ancient visit.");
        for (int i = 0; i < names.Length; i++)
        {
            var stream = RavenStorageHooks.Rng(restored, names[i]);
            for (int draw = 0; draw < 8; draw++)
                Check(stream.NextUnsignedLong() == legacyStreams[i].NextUnsignedLong(), $"Restored raven.{names[i]} sequence diverged.");
        }
    }
}

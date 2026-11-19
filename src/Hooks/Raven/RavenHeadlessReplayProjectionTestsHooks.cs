using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using STS2RitsuLib.RunData;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static SerializableRun CreateReplayRunDocument(Player player, RunState run)
    {
        var acts = run.Acts.Select(act =>
        {
            var saved = act.ToSave();
            // CreateForTest does not populate the room tables. Empty default fields are omitted by
            // the native JSON serializer, while FromSave requires these three enumerable tables.
            var rooms = saved.SerializableRooms;
            rooms.EventIds = [act.AllEvents.First().Id];
            rooms.NormalEncounterIds = [act.AllRegularEncounters.First().Id];
            rooms.EliteEncounterIds = [act.AllEliteEncounters.First().Id];
            rooms.BossId = act.AllBossEncounters.First().Id;
            saved.SerializableRooms = rooms;
            return saved;
        }).ToList();
        return new SerializableRun
        {
            SchemaVersion = SaveManager.Instance.GetLatestSchemaVersion<SerializableRun>(),
            Acts = acts, Players = [player.ToSerializable()],
            SerializableRng = run.Rng.ToSerializable(), SerializableOdds = run.Odds.ToSerializable(),
            SerializableSharedRelicGrabBag = run.SharedRelicGrabBag.ToSerializable(),
            ExtraFields = run.ExtraFields.ToSerializable()
        };
    }

    private static void TestReplayProjections()
    {
        int fallbacks = 0, checkedReady = 0;
        Task Next(Exception _) { fallbacks++; return Task.CompletedTask; }
        Task Ready() { checkedReady++; return Task.CompletedTask; }
        var failed = Task.FromException(new InvalidOperationException("injected scene failure"));
        RavenReplayRecoveryHooks.CompleteOrRestoreNative(Task.CompletedTask, () => true, Ready, Next).GetAwaiter().GetResult();
        RavenReplayRecoveryHooks.CompleteOrRestoreNative(failed, () => true, Ready, Next).GetAwaiter().GetResult();
        RavenReplayRecoveryHooks.CompleteOrRestoreNative(Task.CompletedTask, () => true, () => failed, Next).GetAwaiter().GetResult();
        RavenReplayRecoveryHooks.CompleteOrRestoreNative(failed, () => false, Ready, Next).GetAwaiter().GetResult();
        Check(fallbacks == 2 && checkedReady == 1, "A retired operation replaced the current run or a failed view was accepted.");

        var host = JsonNode.Parse("""{"players":[{"gold":10}],"rng":{"count":0},"run_time":6,"pre_finished_room":{}}""")!.AsObject();
        var afterLoot = host.DeepClone().AsObject();
        afterLoot["players"]![0]!["gold"] = 23;
        var ready = afterLoot.DeepClone().AsObject();
        ready["rng"]!["count"] = 2;
        ready["pending"] = new JsonArray("glass-eye", "sea-glass");
        var steps = new[]
        {
            RavenReplayProjectionHooks.Difference("collect-room-loot", host, afterLoot),
            RavenReplayProjectionHooks.Difference("submit-stake", afterLoot, ready)
        };
        var digest = RavenReplayProjectionHooks.Digest(RavenReplayProjectionHooks.Stable(ready));
        var restarted = JsonSerializer.Deserialize<RavenReplayStep[]>(JsonSerializer.Serialize(steps))!;
        var original = host.DeepClone().AsObject();
        original["num_reloads"] = 4;
        var rebuilt = RavenReplayProjectionHooks.Apply(original, restarted, digest);
        Check(rebuilt["num_reloads"]!.GetValue<int>() == 4 && rebuilt["players"]![0]!["gold"]!.GetValue<int>() == 23,
            "Native reload metadata or pre-claim loot was lost.");
        Check(original["players"]![0]!["gold"]!.GetValue<int>() == 10 && original["pending"] == null,
            "Replay mutated the host before a successful admission.");
        Check(JsonNode.DeepEquals(rebuilt, RavenReplayProjectionHooks.Apply(original, restarted, digest)),
            "Repeated native reload changed the locked outcomes.");
        void MustReject(JsonObject source, RavenReplayStep[] sequence)
        {
            try { RavenReplayProjectionHooks.Apply(source, sequence, digest); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Incomplete or divergent replay was accepted.");
        }
        var other = host.DeepClone().AsObject();
        other["players"]![0]!["gold"] = 11;
        MustReject(other, steps);
        MustReject(host, [steps[0]]);
        MustReject(host, [steps[0], steps[0], steps[1]]);
        Check(JsonNode.DeepEquals(host["pre_finished_room"], rebuilt["pre_finished_room"]),
            "Replay replaced the host room descriptor.");

        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 44532UL);
        var run = (RunState)CreatePoolFixtureRun(player);
        var lamp = RavenStorageHooks.Get(player);
        RavenSessionHooks.Enqueue(lamp, 2, "0:1:3");
        var stake = ModelDb.Relic<Strawberry>().ToMutable();
        var glass = (SeaGlass)ModelDb.Relic<SeaGlass>().ToMutable();
        glass.CharacterId = ModelDb.Character<Ironclad>().Id;
        lamp.Pending = new RavenPendingRound
        {
            SettlementVersion = RavenTransactionHooks.CurrentVersion,
            Outcome = RavenOutcome.Swap, StakeSnapshot = stake.ToSerializable(), StakeConsumed = true,
            Candidates = [glass.ToSerializable()]
        };
        RavenStorageHooks.Touch(player);
        var stream = RavenStorageHooks.Rng(player, "reward");
        stream.NextUnsignedLong();
        var native = CreateReplayRunDocument(player, run);
        var registry = typeof(RunSavedDataStore).Assembly.GetType("STS2RitsuLib.RunData.RunSavedDataRegistry", true)!;
        var payload = (string)registry.GetMethod("BuildPayload")!.Invoke(null, [run])!;
        registry.GetMethod("AttachDocumentFromJson")!.Invoke(null, [native, payload]);
        var encoded = RavenReplayProjectionHooks.Encode(native);
        Check(encoded["_ritsulib"] != null, "The native snapshot discarded its RitsuLib payload.");
        var restored = RunState.FromSerializable(RavenReplayProjectionHooks.Decode(encoded)).Players.Single();
        var resumed = RavenStorageHooks.Get(restored);
        var record = new RavenReplayRecord(1, RavenReplayLogHooks.GameBuild, "Elite", 3, 4,
            glass.Id.ToString(), 0, [.. steps], digest);
        RavenReplayLoadHooks.Validate(record, resumed, 0, 1);
        var restoredGlass = (SeaGlass)RelicModel.FromSerializable(resumed.Pending!.Candidates.Single());
        Check(restoredGlass.CharacterId == glass.CharacterId && resumed.Pending.StakeConsumed
            && RavenStorageHooks.Rng(restored, "reward").NextUnsignedLong() == stream.NextUnsignedLong(),
            "Card pool, stake payment or the next lamp reward draw changed.");
        resumed.InFlight = true;
        try { RavenReplayLoadHooks.Validate(record, resumed, 0, 1); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("A partially completed pickup was admitted.");
    }
}

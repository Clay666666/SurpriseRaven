using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using STS2RitsuLib.RunData;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestReplayContinue()
    {
        foreach (var type in new[] { RoomType.Monster, RoomType.Elite, RoomType.Treasure })
            CheckContinue(type);
    }

    private static void CheckContinue(RoomType type)
    {
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 44595UL);
        var run = (RunState)CreatePoolFixtureRun(player);
        var stake = ModelDb.Relic<StrikeDummy>().ToMutable();
        player.AddRelicInternal(stake, silent: true);
        var reward = ModelDb.Relic<Akabeko>().ToMutable();
        var earned = ModelDb.Relic<BeatingRemnant>().ToMutable();
        var card = run.CreateCard<Eradicate>(player);
        var encounter = type == RoomType.Treasure ? null : ModelDb.AllEncounters.First(model => model.RoomType == type);
        var native = CreateReplayRunDocument(player, run);
        native.StartTime = 44595;
        native.VisitedMapCoords = [new MapCoord(2, 0)];
        native.MapPointHistory = [[new MapPointHistoryEntry
        {
            Rooms = [new MapPointRoomHistoryEntry { RoomType = type, ModelId = encounter?.Id }]
        }]];
        native.PreFinishedRoom = encounter is null ? null : new SerializableRoom
        {
            RoomType = type, EncounterId = encounter.Id, IsPreFinished = true
        };
        var room = native.PreFinishedRoom is { } finished
            ? (AbstractRoom)CombatRoom.FromSerializable(finished, run)
            : new TreasureRoom(0);
        RavenReplayRoomHooks.RoomIdentity.SetValue(room, (int?)3);
        var host = RavenReplayProjectionHooks.Encode(native);
        var root = ProjectSettings.GlobalizePath($"user://raven-replay-fixture/{Guid.NewGuid():N}");
        string profile = Path.Combine(root, "profile", "current_run.save");
        string Anchor(JsonObject checkpoint, string path)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(checkpoint.ToJsonString()));
            return RavenReplayJournalHooks.CreateKey(path, stream);
        }
        string anchor = Anchor(host, profile);
        var history = new RavenReplayLogHooks.History(anchor, host, room);
        var state = RavenStorageHooks.Get(player);
        const string roomKey = "0:1:3";
        var registry = typeof(RunSavedDataStore).Assembly.GetType("STS2RitsuLib.RunData.RunSavedDataRegistry", true)!;
        void Publish(string operation, string candidate = "", int index = -1)
        {
            RavenStorageHooks.Touch(player);
            var live = CreateReplayRunDocument(player, run);
            live.StartTime = native.StartTime;
            live.VisitedMapCoords = native.VisitedMapCoords;
            live.MapPointHistory = native.MapPointHistory;
            if (encounter is not null) live.Players[0].Deck.Add(card.ToSerializable());
            var payload = (string)registry.GetMethod("BuildPayload")!.Invoke(null, [run])!;
            registry.GetMethod("AttachDocumentFromJson")!.Invoke(null, [live, payload]);
            var record = history.Capture(RavenReplayProjectionHooks.Encode(live), operation, 4, candidate, index)!;
            RavenReplayJournalHooks.SaveAt(root, anchor, JsonSerializer.Serialize(record));
        }
        void ContinueTwice(Action<Player, RavenState> assert, Action<RavenReplayLoadHooks.Session>? checkView = null)
        {
            var hostAfterContinue = host.DeepClone().AsObject();
            for (int count = 1; count <= 2; count++)
            {
                hostAfterContinue["num_reloads"] = count;
                var saved = RavenReplayProjectionHooks.Decode(hostAfterContinue);
                var original = RunState.FromSerializable(saved);
                string key = Anchor(hostAfterContinue, profile);
                string data = RavenReplayJournalHooks.ReadAt(root, key)!;
                var resumed = RavenReplayLoadHooks.Reconstruct(original, saved, key, data);
                var restored = resumed.Restored.Players.Single();
                Check(resumed.Room.RoomType == type && resumed.Room.Id == 3 && resumed.Restored.NextRoomId == 4
                    && original.Players.Single().Relics.Any(relic => relic.Id == stake.Id),
                    $"{type}: continue changed the room or mutated the original checkpoint.");
                Check(restored.Relics.Count(relic => relic.Id == earned.Id) == 1 && restored.Gold == player.Gold,
                    $"{type}: continue did not preserve exactly one set of source rewards.");
                Check(restored.Deck.Cards.Count(model => model.Id == card.Id) == (encounter is null ? 0 : 1),
                    $"{type}: a confirmed room card was lost or duplicated.");
                assert(restored, RavenStorageHooks.Get(restored));
                checkView?.Invoke(resumed);
            }
        }

        Check(!RavenReplayRoomHooks.AllRewardsSettled(room, false, false, false, []),
            $"{type}: unfinished source could publish a replay.");
        player.Gold += 48;
        player.AddRelicInternal(earned, silent: true);
        RavenSessionHooks.Enqueue(state, 4, roomKey);
        Check(RavenReplayRoomHooks.AllRewardsSettled(room, encounter is not null, encounter is null, false, []),
            $"{type}: closing a settled rewards screen lost the completion evidence.");
        Publish("lamp-open");
        var pending = new RavenPendingRound
        {
            SettlementVersion = RavenTransactionHooks.CurrentVersion, Outcome = RavenOutcome.Upgrade,
            StakeId = stake.Id.ToString(), StakeSnapshot = stake.ToSerializable(), Candidates = [reward.ToSerializable()], NextLights = 5
        };
        Check(RavenTransactionHooks.CommitAsync(state, pending, () =>
        {
            player.RemoveRelicInternal(stake, silent: true);
            return Task.CompletedTask;
        }, () => { }).GetAwaiter().GetResult(), "Detached sacrifice failed.");
        var rng = RavenStorageHooks.Rng(player, "reward");
        rng.NextUnsignedLong();
        Publish("before-pickup", reward.Id.ToString(), 0);
        ContinueTwice((restored, lamp) => Check(lamp is { Round: 0, Pending.StakeConsumed: true }
            && lamp.Pending.Candidates.Single().Id == reward.Id && restored.Relics.All(relic => relic.Id != stake.Id),
            $"{type}: pending continue changed the sacrifice or locked candidate."));

        Check(RavenTransactionHooks.CollectAsync(state, pending, 0, _ => true, _ =>
        {
            player.AddRelicInternal(reward, silent: true);
            return Task.CompletedTask;
        }, () => { }).GetAwaiter().GetResult() == RavenClaimResult.Claimed, "Detached claim failed.");
        Publish("collect");
        ulong expectedDraw = rng.NextUnsignedLong();
        ContinueTwice((restored, lamp) => Check(lamp is { Round: 1, Lights: 5, Pending: null, OpportunityId: 1 }
            && restored.Relics.Count(relic => relic.Id == reward.Id) == 1 && restored.Relics.All(relic => relic.Id != stake.Id)
            && RavenStorageHooks.Rng(restored, "reward").NextUnsignedLong() == expectedDraw,
            $"{type}: collected reward, next round or next RNG draw was repeated or lost."));
        var swallowed = new RavenPendingRound
        {
            SettlementVersion = RavenTransactionHooks.CurrentVersion, Outcome = RavenOutcome.Swallow,
            StakeId = reward.Id.ToString(), StakeSnapshot = reward.ToSerializable(),
            StakeTitle = reward.Title.GetFormattedText()
        };
        Check(RavenTransactionHooks.CommitAsync(state, swallowed, () =>
        {
            player.RemoveRelicInternal(reward, silent: true);
            return Task.CompletedTask;
        }, () => { }).GetAwaiter().GetResult(), "Detached swallow failed.");
        Publish("SubmitStakeAsync");
        if (type is RoomType.Elite or RoomType.Treasure)
            ContinueTwice((restored, lamp) => Check(lamp.Pending is { Outcome: RavenOutcome.Swallow, StakeConsumed: true }
                && restored.Relics.All(relic => relic.Id != reward.Id), $"{type}: committed swallow did not restore."),
                session => CheckSwallowView(session, swallowed.StakeTitle));
        Check(RavenTransactionHooks.CompleteSwallow(state), "Detached swallow did not finish.");
        Publish("ResolveSwallowAsync");
        var afterSwallow = RavenStorageHooks.Rng(player, "reward").ToSerializable();
        void CheckSettled(Player restored, RavenState lamp) => Check(!lamp.HasOpportunity && lamp.Pending is null
            && lamp.EndedBySwallow && lamp.Lights == 0 && restored.Relics.All(relic => relic.Id != reward.Id)
            && RavenStorageHooks.Rng(restored, "reward").ToSerializable() == afterSwallow,
            $"{type}: a swallowed relic, active opportunity or changed RNG was restored.");
        ContinueTwice(CheckSettled, session =>
        {
            if (type == RoomType.Monster) Check(session.SwallowResult is null, "Normal combat restored a terminal overlay.");
            else CheckSwallowView(session, swallowed.StakeTitle);
        });
        // Leaving the mounted view does not change the room's settled replay.
        Publish("save-and-quit");
        if (type is RoomType.Elite or RoomType.Treasure)
            ContinueTwice(CheckSettled, session => CheckSwallowView(session, swallowed.StakeTitle));

        var nextFloor = RavenReplayProjectionHooks.Decode(host);
        nextFloor.VisitedMapCoords.Add(new MapCoord(2, 1));
        Check(RavenReplayJournalHooks.ReadAt(root, Anchor(RavenReplayProjectionHooks.Encode(nextFloor), profile)) is null,
            "The next floor received the old swallow continuation.");
        RavenSessionHooks.Enqueue(state, 1, roomKey);
        Publish("lamp-open");
        ContinueTwice((_, lamp) => Check(lamp.HasOpportunity && !lamp.EndedBySwallow,
            $"{type}: a new opportunity inherited the old swallow."), session =>
            Check(session.SwallowResult is null, $"{type}: new opportunity restored a terminal overlay."));
        RavenTransactionHooks.Abandon(state);
        Publish("ExitAsync");
        ContinueTwice((_, lamp) => Check(!lamp.HasOpportunity && !lamp.EndedBySwallow,
            $"{type}: a voluntary exit was marked as a swallow."), session =>
            Check(session.SwallowResult is null, $"{type}: a voluntary exit restored a terminal overlay."));
        Check(RavenReplayJournalHooks.ReadAt(root, Anchor(host, Path.Combine(root, "other", "current_run.save"))) is null,
            "A different profile received another run's continuation.");
        Check(!RunManager.Instance.IsInProgress, "Replay fixture started a live run.");
    }
}

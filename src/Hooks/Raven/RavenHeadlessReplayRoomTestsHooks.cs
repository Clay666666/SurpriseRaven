using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    internal static void TestReplayRoomRules()
    {
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 4453UL);
        var run = (RunState)CreatePoolFixtureRun(player);
        var record = new RavenReplayRecord(1, RavenReplayLogHooks.GameBuild, "", 3, 4, "", -1, [], "");
        var doc = new SerializableRun();
        var entry = new MapPointRoomHistoryEntry { RoomType = RoomType.Treasure };
        var history = new MapPointHistoryEntry { Rooms = [entry] };
        doc.MapPointHistory = [[history]];
        AbstractRoom Restore() => RavenReplayRoomHooks.RestoreRoom(record with { Room = entry.RoomType.ToString() }, doc, run);
        void Refuses(string reason)
        {
            try { Restore(); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException(reason);
        }

        var rewards = new NRewardsScreen();
        try
        {
            var chest = Restore();
            Check(chest is TreasureRoom && chest.Id == 3 && run.NextRoomId == 4, "Treasure restored another room identity.");
            Check(!RavenReplayRoomHooks.AllRewardsSettled(chest, false, false, false, [])
                && !RavenReplayRoomHooks.AllRewardsSettled(chest, false, true, false, [rewards])
                && RavenReplayRoomHooks.AllRewardsSettled(chest, false, true, false, []),
                "An unawarded chest, second chest round, or uncollected bonus reward was lost.");

            foreach (var type in new[] { RoomType.Elite, RoomType.Monster })
            {
                var encounter = ModelDb.AllEncounters.First(e => e.RoomType == type);
                entry.RoomType = type;
                entry.ModelId = encounter.Id;
                doc.PreFinishedRoom = new SerializableRoom { IsPreFinished = true, RoomType = type, EncounterId = encounter.Id };
                var room = Restore();
                Check(room is CombatRoom && room.ModelId == encounter.Id && room.Id == 3 && run.NextRoomId == 4,
                    "The actual pre-finished encounter or its saved room identity changed.");
                Check(!RavenReplayRoomHooks.AllRewardsSettled(room, false, false, false, [])
                    && !RavenReplayRoomHooks.AllRewardsSettled(room, true, false, false, [rewards]),
                    "An empty stack or unfinished reward set must keep the native room checkpoint.");
                AccessTools.PropertySetter(typeof(NRewardsScreen), nameof(NRewardsScreen.IsComplete)).Invoke(rewards, [true]);
                Check(RavenReplayRoomHooks.AllRewardsSettled(room, false, false, false, [rewards])
                    && RavenReplayRoomHooks.AllRewardsSettled(room, true, false, false, []),
                    "Finished terminal rewards should survive auto-proceed closing their screen.");
                AccessTools.PropertySetter(typeof(NRewardsScreen), nameof(NRewardsScreen.IsComplete)).Invoke(rewards, [false]);
                doc.PreFinishedRoom.ParentEventId = ModelDb.AllEvents.First().Id;
                Refuses("An embedded event combat was admitted with its parent event unfinished.");
                doc.PreFinishedRoom.ParentEventId = null;
                doc.PreFinishedRoom.IsPreFinished = false;
                Refuses("A combat before victory was accepted.");
            }
            foreach (var type in new[] { RoomType.Shop, RoomType.RestSite, RoomType.Event, RoomType.Boss })
            {
                entry.RoomType = type;
                Refuses("Unsupported interactive source lost its native room continuation.");
            }
            entry.RoomType = RoomType.Treasure;
            entry.ModelId = null;
            doc.PreFinishedRoom = null;
            history.Rooms.Add(new MapPointRoomHistoryEntry { RoomType = RoomType.Event });
            Refuses("A multi-room history was truncated.");
            Check(!RunManager.Instance.IsInProgress, "Checking settled rooms must not start a run.");
        }
        finally { rewards.Free(); }
        TestNativeRavenPreviewCards();
    }
}

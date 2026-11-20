using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SurpriseRaven.Hooks.Raven;

// Decode and validate a detached run first. The host always receives its original save for the
// reload counter write. Only the state used by this admission and its matching scene is replaced.
[HarmonyPatch]
internal static class RavenReplayLoadHooks
{
    internal sealed class Session(RunState original, RunState restored, AbstractRoom room, RavenReplayRecord record,
        string key, JsonObject projected, SerializableRun host, string? swallowResult = null)
    {
        internal RunState Original { get; } = original;
        internal SerializableRun Host { get; } = host;
        internal RunState Restored { get; } = restored;
        internal AbstractRoom Room { get; } = room;
        internal RavenReplayRecord Record { get; } = record;
        internal string Key { get; } = key;
        internal JsonObject Projected { get; } = projected;
        internal string? SwallowResult { get; } = swallowResult;
        internal bool Entered { get; set; }
        private bool _requestedContinuation;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Ready => _ready.Task;
        internal void SignalReady() => _ready.TrySetResult();
        internal void SignalFailure(Exception exception) => _ready.TrySetException(exception);

        internal void RequestContinuation(Action openMap, Action deferMap, Action<string> showResult)
        {
            if (_requestedContinuation) return;
            _requestedContinuation = true;
            if (SwallowResult is { } message)
            {
                deferMap();
                showResult(message);
            }
            else openMap();
        }

        internal bool CompleteContinuation(bool hasOpportunity, bool mapOpen, bool mapDeferred, RavenLampPanelHooks? panel)
        {
            bool hasPanel = mapDeferred && RavenOverlayHooks.IsMounted(panel);
            bool ready = SwallowResult is not null ? !mapOpen && hasPanel && panel!.IsTerminal
                : hasPanel || (!hasOpportunity && mapOpen);
            if (ready) SignalReady();
            return ready;
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RunState, object> Retired = new();
    internal static Session? Active { get; private set; }
    internal static void AllowNativeRetry(RunState original) => Retired.Remove(original);
    internal static void Retire(RunState state) => Retired.GetOrCreateValue(state);
    internal static bool IsRestoredRoom(AbstractRoom room) => ReferenceEquals(Active?.Room, room);
    internal static bool AwaitingRoom(Player player) => Active is { Entered: false } session
        && ReferenceEquals(player.RunState, session.Restored);
    internal static bool IsRestoringSwallow(Player player) => Active is { Entered: true, SwallowResult: not null } session
        && !session.Ready.IsCompleted && ReferenceEquals(player.RunState, session.Restored)
        && ReferenceEquals(player.RunState.CurrentRoom, session.Room);

    internal static void Validate(RavenReplayRecord record, RavenState state, int act, int floor)
    {
        if (record.Format != 1 || record.GameBuild != RavenReplayLogHooks.GameBuild
            || record.RoomId < 0 || record.NextRoomId <= record.RoomId || record.Steps.Count == 0
            || state.InFlight || state.Faulted || (!state.HasOpportunity && (state.OpportunityId == 0 || state.Pending is not null))
            || state.RoomKey != $"{act}:{floor}:{record.RoomId}"
            || state.Pending is { } pending && !RavenTransactionHooks.IsReady(pending))
            throw new InvalidDataException("Incompatible, incomplete or stale lamp boundary.");
        if (record.CandidateIndex == -1 && record.Candidate.Length == 0) return;
        var candidates = state.Pending?.Candidates;
        if (!state.HasOpportunity || candidates == null || record.CandidateIndex < 0 || record.CandidateIndex >= candidates.Count
            || candidates[record.CandidateIndex].Id?.ToString() != record.Candidate)
            throw new InvalidDataException("The selected reward no longer matches the locked round.");
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedSingleplayer)), HarmonyPrefix]
    private static void RestoreFromNativeSave(ref RunState state, SerializableRun save)
    {
        Active = null;
        if (RavenReplayRecoveryHooks.RestoringNative) return;
        try
        {
            var key = RavenReplayJournalHooks.HostKey();
            string? payload = RavenReplayJournalHooks.Read(key);
            if (payload is null)
            {
                Logger.Log($"[Raven] No lamp replay for native checkpoint anchor={key[^12..]}.");
                return;
            }
            var session = Reconstruct(state, save, key, payload);
            RavenReplayLogHooks.Rebind(session.Restored.Players[0], key, session.Record, session.Projected, session.Room);
            Active = session;
            state = session.Restored;
            var lamp = RavenStorageHooks.Get(state.Players[0]);
            Logger.Log($"[Raven] Replayed {session.Record.Steps.Count} confirmed steps for {session.Record.Room} room={session.Room.Id}, " +
                $"round={lamp.Round}, open={lamp.HasOpportunity}, anchor={key[^12..]}.");
        }
        catch (Exception error)
        {
            Active = null;
            Logger.Log($"[Raven] Lamp replay rejected; continuing from the native room checkpoint. {error.Message}");
        }
    }

    internal static Session Reconstruct(RunState original, SerializableRun save, string key, string payload)
    {
        var record = JsonSerializer.Deserialize<RavenReplayRecord>(payload)
            ?? throw new InvalidDataException("Empty lamp replay.");
        if (record.Format != 1 || record.GameBuild != RavenReplayLogHooks.GameBuild || record.Steps.Count == 0)
            throw new InvalidDataException("Replay version does not match the running game.");
        var host = RavenReplayProjectionHooks.Encode(save);
        var projected = RavenReplayProjectionHooks.Apply(host, record.Steps, record.FinalDigest);
        var projectedSave = RavenReplayProjectionHooks.Decode(projected);
        var restored = RunState.FromSerializable(projectedSave);
        if (restored.Players.Count != 1 || restored.CurrentMapCoord is not { } coord)
            throw new InvalidDataException("Expected a single player and visited room.");
        var state = RavenStorageHooks.Get(restored.Players[0]);
        Validate(record, state, restored.CurrentActIndex, coord.row + 1);
        var room = RavenReplayRoomHooks.RestoreRoom(record, projectedSave, restored);
        return new Session(original, restored, room, record, key, projected, save, SwallowResult(room, state));
    }

    private static string? SwallowResult(AbstractRoom room, RavenState state)
    {
        if (room.RoomType is not (RoomType.Elite or RoomType.Treasure)
            || state.DeferredOpportunities.Any(offer => offer.RoomKey == state.RoomKey)) return null;
        if (!state.HasOpportunity && state.EndedBySwallow) return state.LastMessage;
        return state.PendingLights.Count == 1 && state.Pending is { Outcome: RavenOutcome.Swallow } pending
            ? RavenTransactionHooks.SwallowMessage(pending) : null;
    }

    [HarmonyPatch(typeof(NGame), nameof(NGame.LoadRun)), HarmonyPrefix]
    private static bool RestoreMatchingScene(ref RunState runState, ref SerializableRoom? preFinishedRoom,
        ref Task __result)
    {
        if (Retired.TryGetValue(runState, out _))
        {
            __result = Task.CompletedTask;
            return false;
        }
        if (Active is not { } session || !ReferenceEquals(runState, session.Original)) return true;
        runState = session.Restored;
        // CombatRoom and TreasureRoom have already been constructed and checked in admission.
        preFinishedRoom = null;
        return true;
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.LoadIntoLatestMapCoord)), HarmonyPrefix]
    private static void SupplySourceRoom(RunManager __instance, ref AbstractRoom? preFinishedRoom)
    {
        if (Active is { Entered: false } session && ReferenceEquals(__instance.DebugOnlyGetState(), session.Restored))
            preFinishedRoom = session.Room;
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp)), HarmonyPrefix]
    private static void DetachReplaySession()
    {
        if (Active is { } previous)
        {
            previous.SignalReady();
            Retired.GetOrCreateValue(previous.Original);
            Retired.GetOrCreateValue(previous.Restored);
        }
        Active = null;
        RavenReplayLogHooks.OnCleanup();
        RavenReplayRoomHooks.Reset();
    }

    [HarmonyPatch(typeof(RavenServiceHooks), "Tick"), HarmonyPrefix]
    private static bool WaitForRoomToEnter() => Active is not { Entered: false } session
        || !ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), session.Restored);

    [HarmonyPatch(typeof(RavenServiceHooks), "Tick"), HarmonyPostfix]
    private static void RestoreRoomContinuation() => RavenReplayRoomHooks.ResumeMapWhenBound();
}

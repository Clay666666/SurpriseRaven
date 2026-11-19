using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using System.Security.Cryptography;
using System.Text;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SurpriseRaven.Hooks.Raven;

internal static class RavenReplayLogHooks
{
    internal sealed class History(string key, JsonObject host, AbstractRoom room)
    {
        public string Key { get; } = key;
        public JsonObject Current = host;
        public AbstractRoom Room { get; } = room;
        public List<RavenReplayStep> Steps { get; } = [];

        internal RavenReplayRecord? Capture(JsonObject observed, string operation, int nextRoomId,
            string candidate = "", int index = -1)
        {
            if (Room.Id is not int roomId || roomId < 0 || nextRoomId <= roomId)
                throw new InvalidDataException("The native room has no stable identity.");
            var step = RavenReplayProjectionHooks.Difference(operation, Current, observed);
            if (step.Changes.Count != 0 || operation == "before-pickup") Steps.Add(step);
            Current = observed;
            return Steps.Count == 0 ? null : new RavenReplayRecord(1, GameBuild, Room.RoomType.ToString(), roomId,
                nextRoomId, candidate, index, [.. Steps],
                RavenReplayProjectionHooks.Digest(RavenReplayProjectionHooks.Stable(Current)));
        }
    }

    internal sealed record Claim(Player Player, string Key, AbstractRoom Room);
    private static readonly ConditionalWeakTable<Player, History> Histories = new();
    private static Claim? _armed;
    private static readonly Lazy<string> RuntimeIdentity = new(() =>
    {
        var mods = ModManager.GetLoadedMods().Select(mod => new
        {
            id = mod.manifest?.id, version = mod.manifest?.version,
            assemblies = mod.assemblies?.Select(a => a.ManifestModule.ModuleVersionId).Order().ToArray()
        }).ToArray();
        var identity = JsonSerializer.Serialize(new { game = typeof(RunState).Assembly.ManifestModule.ModuleVersionId, mods });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    });
    internal static string GameBuild => RuntimeIdentity.Value;
    internal static bool RoomKind(AbstractRoom? room) => RavenReplayRoomHooks.SupportsRoom(room);

    internal static void Mark(Player player, string operation)
    {
        if (!RavenServiceHooks.IsSingleplayer || !RunManager.Instance.ShouldSave
            || !ReferenceEquals(player.RunState, RunManager.Instance.DebugOnlyGetState())
            || !RoomKind(player.RunState.BaseRoom)) return;
        try
        {
            var state = RavenStorageHooks.Get(player);
            if (RavenServiceHooks.Busy || state.InFlight || RavenReplayLoadHooks.AwaitingRoom(player)) return;
            if (state.RoomKey != RavenServiceHooks.RoomKey(player))
            {
                Disarm(player);
                return;
            }
            if (state.Faulted)
            {
                Logger.Log($"[Raven] Lamp replay kept its last confirmed boundary at {operation}: faulted transaction.");
                return;
            }
            if (state.Pending is { } pending && !RavenTransactionHooks.IsReady(pending)) return;
            if (state.OpportunityId == 0 && !state.HasOpportunity) return;
            if (RavenReplayRoomHooks.IsSettled(player, out string reason)) Publish(player, operation);
            else Logger.Log($"[Raven] Lamp replay deferred at {operation}: {reason}.");
        }
        catch (Exception error)
        {
            Histories.Remove(player);
            Logger.Log($"[Raven] Lamp replay recording unavailable at {operation}: {error.Message}");
        }
    }

    private static History GetHistory(Player player)
    {
        var run = RunManager.Instance;
        var room = player.RunState.BaseRoom!;
        var key = RavenReplayJournalHooks.HostKey();
        if (!Histories.TryGetValue(player, out var history) || history.Key != key || history.Room != room)
        {
            var read = SaveManager.Instance.LoadRunSave();
            if (!read.Success || read.SaveData is null) throw new InvalidDataException("Native room checkpoint unavailable.");
            var native = read.SaveData;
            var live = run.ToSave(null);
            if (native.StartTime != live.StartTime || !native.VisitedMapCoords.SequenceEqual(live.VisitedMapCoords)
                || native.CurrentActIndex != live.CurrentActIndex || native.Players.Count != 1 || live.Players.Count != 1
                || native.SerializableRng.Seed != live.SerializableRng.Seed)
                throw new InvalidDataException("The native file belongs to a different location or run.");
            history = new(key, RavenReplayProjectionHooks.Encode(native), room);
            Histories.Remove(player);
            Histories.Add(player, history);
        }
        return history;
    }

    internal static Claim? BeforeClaim(Player player, RavenPendingRound pending, RavenPickupRequest request)
    {
        if (!RavenServiceHooks.IsSingleplayer || !RunManager.Instance.ShouldSave) return null;
        try
        {
            if (request.Kind != RavenPickupKind.Candidate || request.CandidateIndex < 0
                || request.CandidateIndex >= pending.Candidates.Count) return null;
            var candidate = pending.Candidates[request.CandidateIndex];
            if (candidate.Id is null || !RavenReplayRoomHooks.IsSettled(player, out _)) return null;
            return Publish(player, "before-pickup", candidate.Id.ToString(), request.CandidateIndex);
        }
        catch (Exception error)
        {
            Logger.Log($"[Raven] Using native SL for this pickup: {error.Message}");
            return null;
        }
    }

    private static Claim? Publish(Player player, string operation, string candidate = "", int index = -1)
    {
        if (player.RunState is not RunState run) return null;
        var history = GetHistory(player);
        var observed = RavenReplayProjectionHooks.Encode(RunManager.Instance.ToSave(null));
        var record = history.Capture(observed, operation, run.NextRoomId, candidate, index);
        if (record is null) return null;
        RavenReplayJournalHooks.Save(history.Key, JsonSerializer.Serialize(record));
        var state = RavenStorageHooks.Get(player);
        Logger.Log($"[Raven] Lamp replay saved at {operation}: {record.Room} room={record.RoomId}, revision={state.Revision}, " +
            $"round={state.Round}, open={state.HasOpportunity}, steps={record.Steps.Count}, anchor={history.Key[^12..]}.");
        return _armed = new Claim(player, history.Key, history.Room);
    }


    internal static void Disarm(Player player)
    {
        if (_armed is not { } claim || !ReferenceEquals(player, claim.Player)) return;
        _armed = null;
        try
        {
            RavenReplayJournalHooks.Discard(claim.Key);
            Logger.Log("[Raven] Discarded the lamp replay after a room change.");
        }
        catch (Exception error) { Logger.Log($"[Raven] Cannot clear lamp replay: {error.Message}"); }
    }

    internal static void Rebind(Player player, string key, RavenReplayRecord record, JsonObject state, AbstractRoom room)
    {
        var history = new History(key, state, room);
        history.Steps.AddRange(record.Steps);
        Histories.Remove(player);
        Histories.Add(player, history);
        _armed = new(player, key, room);
    }

    internal static bool ClaimsNativeCheckpoint()
    {
        if (_armed is not { } claim || !ReferenceEquals(claim.Player.RunState, RunManager.Instance.DebugOnlyGetState()))
            return false;
        if (RavenReplayLoadHooks.Active is { } session && ReferenceEquals(session.Restored, claim.Player.RunState)
            && !session.Ready.IsCompletedSuccessfully) return true;
        return ReferenceEquals(claim.Player.RunState.BaseRoom, claim.Room)
            && (RavenServiceHooks.Busy || RavenStorageHooks.Get(claim.Player).InFlight);
    }

    internal static void OnCleanup() => _armed = null;
}

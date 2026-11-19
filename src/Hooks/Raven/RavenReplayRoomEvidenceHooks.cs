using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenReplayRoomHooks
{
    private static AbstractRoom? _awardedChest, _completedCombat, _terminalRoom;
    private static NRewardsScreen? _terminalRewards, _acceptedRewards;
    private static readonly System.Reflection.FieldInfo? Screens = AccessTools.Field(typeof(NOverlayStack), "_overlays");

    internal static bool SupportsRoom(AbstractRoom? room) => room is TreasureRoom
        or CombatRoom { RoomType: RoomType.Elite or RoomType.Monster, IsPreFinished: true, ParentEventId: null };

    [HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), "AwardRelics"), HarmonyPostfix]
    private static void RememberAwardedChest() =>
        _awardedChest = RunManager.Instance.DebugOnlyGetState()?.BaseRoom;

    [HarmonyPatch(typeof(NRewardsScreen), "UpdateScreenState"), HarmonyPostfix]
    private static void RememberTerminalCompletion(NRewardsScreen __instance, IRunState ____runState, bool ____isTerminal)
    {
        if (!____isTerminal || !ReferenceEquals(____runState, RunManager.Instance.DebugOnlyGetState())
            || ____runState?.CurrentRoom is not CombatRoom room || !ReferenceEquals(room, ____runState.BaseRoom)
            || !SupportsRoom(room)) return;
        _terminalRewards = __instance;
        _terminalRoom = room;
        if (__instance.IsComplete) RememberCombat(room, "terminal rewards complete");
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.ProceedFromTerminalRewardsScreen)), HarmonyPrefix]
    private static void RememberAcceptedTerminalProceed(RunManager __instance)
    {
        var run = __instance.DebugOnlyGetState();
        if (run?.BaseRoom is CombatRoom room && ReferenceEquals(room, run.CurrentRoom)
            && ReferenceEquals(room, _terminalRoom)
            && _terminalRewards is not null && NOverlayStack.Instance is { ScreenCount: > 0 } stack
            && ReferenceEquals(stack.Peek(), _terminalRewards))
        {
            _acceptedRewards = _terminalRewards;
            RememberCombat(room, "terminal proceed accepted");
        }
    }

    private static void RememberCombat(AbstractRoom room, string evidence)
    {
        if (ReferenceEquals(_completedCombat, room)) return;
        _completedCombat = room;
        Logger.Log($"[Raven] Replay source settled: {room.RoomType}, room={room.Id}, {evidence}.");
    }

    internal static bool IsSettled(Player player, out string reason)
    {
        var room = player.RunState.BaseRoom;
        if (!SupportsRoom(room) || !ReferenceEquals(room, player.RunState.CurrentRoom))
        {
            reason = "unsupported or nested source room";
            return false;
        }
        if (NOverlayStack.Instance is not { } stack || Screens?.GetValue(stack) is not IEnumerable<IOverlayScreen> overlays)
        {
            reason = "reward stack unavailable";
            return false;
        }
        bool chestFinished = room is TreasureRoom && ReferenceEquals(room, _awardedChest)
            && RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics is null;
        bool combatFinished = ReferenceEquals(room, _completedCombat);
        bool finished = AllRewardsSettled(room!, combatFinished, chestFinished,
            RavenReplayLoadHooks.IsRestoredRoom(room!), overlays, combatFinished ? _acceptedRewards : null);
        reason = finished ? "room rewards settled" : "unconfirmed rewards or another overlay";
        return finished;
    }

    internal static bool AllRewardsSettled(AbstractRoom room, bool combatCompleted, bool chestFinished,
        bool restored, IEnumerable<IOverlayScreen> overlays, NRewardsScreen? acceptedRewards = null)
    {
        if (!SupportsRoom(room)) return false;
        foreach (var screen in overlays)
        {
            if (screen is RavenLampPanelHooks) continue;
            // IsComplete only becomes true for a terminal set with no remaining reward buttons.
            if (screen is not NRewardsScreen rewards || (!rewards.IsComplete && !ReferenceEquals(rewards, acceptedRewards)))
                return false;
            combatCompleted = true;
        }
        return restored || (room is TreasureRoom ? chestFinished : combatCompleted);
    }

    internal static void Reset()
    {
        _awardedChest = _completedCombat = _terminalRoom = null;
        _terminalRewards = _acceptedRewards = null;
    }
}

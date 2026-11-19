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
    private static AbstractRoom? _awardedChest, _completedCombat;
    private static readonly System.Reflection.FieldInfo? Screens = AccessTools.Field(typeof(NOverlayStack), "_overlays");

    internal static bool SupportsRoom(AbstractRoom? room) => room is TreasureRoom
        or CombatRoom { RoomType: RoomType.Elite or RoomType.Monster, IsPreFinished: true, ParentEventId: null };

    [HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), "AwardRelics"), HarmonyPostfix]
    private static void RememberAwardedChest() =>
        _awardedChest = RunManager.Instance.DebugOnlyGetState()?.BaseRoom;

    [HarmonyPatch(typeof(NRewardsScreen), "UpdateScreenState"), HarmonyPostfix]
    private static void RememberTerminalCompletion(NRewardsScreen __instance, IRunState ____runState, bool ____isTerminal)
    {
        if (____isTerminal && __instance.IsComplete && ReferenceEquals(____runState, RunManager.Instance.DebugOnlyGetState())
            && ____runState?.CurrentRoom is CombatRoom room && ReferenceEquals(room, ____runState.BaseRoom))
            _completedCombat = room;
    }

    internal static bool IsSettled(Player player)
    {
        var room = player.RunState.BaseRoom;
        if (!SupportsRoom(room) || !ReferenceEquals(room, player.RunState.CurrentRoom)
            || NOverlayStack.Instance is not { } stack
            || Screens?.GetValue(stack) is not IEnumerable<IOverlayScreen> overlays) return false;
        bool chestFinished = room is TreasureRoom && ReferenceEquals(room, _awardedChest)
            && RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics is null;
        return AllRewardsSettled(room!, ReferenceEquals(room, _completedCombat), chestFinished,
            RavenReplayLoadHooks.IsRestoredRoom(room!), overlays);
    }

    internal static bool AllRewardsSettled(AbstractRoom room, bool combatCompleted, bool chestFinished,
        bool restored, IEnumerable<IOverlayScreen> overlays)
    {
        if (!SupportsRoom(room)) return false;
        foreach (var screen in overlays)
        {
            if (screen is RavenLampPanelHooks) continue;
            // IsComplete only becomes true for a terminal set with no remaining reward buttons.
            if (screen is not NRewardsScreen { IsComplete: true }) return false;
            combatCompleted = true;
        }
        return restored || (room is TreasureRoom ? chestFinished : combatCompleted);
    }

    internal static void Reset()
    {
        _awardedChest = _completedCombat = null;
    }
}

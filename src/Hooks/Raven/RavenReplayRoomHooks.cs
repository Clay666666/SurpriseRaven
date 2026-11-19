using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SurpriseRaven.Hooks.Raven;

[HarmonyPatch]
internal static partial class RavenReplayRoomHooks
{
    internal static readonly PropertyInfo NextRoom = typeof(RunState).GetProperty(nameof(RunState.NextRoomId))!;
    internal static readonly PropertyInfo RoomIdentity = typeof(AbstractRoom).GetProperty(nameof(AbstractRoom.Id))!;

    internal static AbstractRoom RestoreRoom(RavenReplayRecord record, SerializableRun projectedSave, RunState restored)
    {
        int act = restored.CurrentActIndex;
        if (record.RoomId < 0 || record.NextRoomId <= record.RoomId || act < 0 || act >= projectedSave.MapPointHistory.Count
            || projectedSave.MapPointHistory[act].LastOrDefault()?.Rooms is not [var visited]
            || record.Room != visited.RoomType.ToString())
            throw new InvalidDataException("The saved floor does not contain the recorded standalone source room.");

        AbstractRoom room;
        if (visited.RoomType == RoomType.Treasure && visited.ModelId is null && projectedSave.PreFinishedRoom is null)
            room = new TreasureRoom(act);
        else if (visited.RoomType is RoomType.Monster or RoomType.Elite
            && projectedSave.PreFinishedRoom is { IsPreFinished: true, ParentEventId: null, EncounterId: { } encounter } saved
            && saved.RoomType == visited.RoomType && encounter == visited.ModelId)
            room = CombatRoom.FromSerializable(saved, restored);
        else throw new InvalidDataException("Unsupported source, unfinished room or changed encounter.");

        // Check private setters on detached state before admitting it into the real run.
        RoomIdentity.SetValue(room, (int?)record.RoomId);
        NextRoom.SetValue(restored, record.NextRoomId);
        if (room.Id != record.RoomId || restored.NextRoomId != record.NextRoomId || !SupportsRoom(room))
            throw new InvalidDataException("The room identity could not be restored.");
        return room;
    }

    [HarmonyPatch(typeof(RunManager), "EnterRoomInternal"), HarmonyPrefix]
    private static void RestoreFinishedRoomFlag(AbstractRoom room, ref bool isRestoringRoomStackBase)
    {
        if (RavenReplayLoadHooks.IsRestoredRoom(room)) isRestoringRoomStackBase = true;
    }

    [HarmonyPatch(typeof(AbstractRoom), nameof(AbstractRoom.Enter)), HarmonyPrefix]
    private static bool EnterSettledRoom(AbstractRoom __instance, IRunState? runState, ref Task __result)
    {
        var session = RavenReplayLoadHooks.Active;
        if (session == null || !ReferenceEquals(session.Room, __instance) || !ReferenceEquals(runState, session.Restored))
            return true;
        __result = EnterSettledRoomAsync(session);
        return false;
    }

    private static async Task EnterSettledRoomAsync(RavenReplayLoadHooks.Session session)
    {
        // NGame installs NRun before LoadIntoLatestMapCoord. Allow its deferred map nodes one tree
        // iteration to enter; a missing scene then faults the host load and uses its native recovery.
        if (NRun.Instance?.IsInsideTree() != true || NMapScreen.Instance?.IsInsideTree() != true)
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        if (!ReferenceEquals(RavenReplayLoadHooks.Active, session)) return;
        if (NRun.Instance is not { } run || !run.IsInsideTree() || NMapScreen.Instance is not { } map || !map.IsInsideTree())
            throw new InvalidOperationException("The host did not install a room and map for the lamp continuation.");
        var shell = ContinueSurface(() =>
        {
            if (ReferenceEquals(RavenReplayLoadHooks.Active, session)
                && ReferenceEquals(session.Restored.CurrentRoom, session.Room)) map.Open();
        });
        try
        {
            run.SetCurrentRoom(shell);
            map.SetTravelEnabled(true);
            RunManager.Instance.ActionExecutor.Unpause();
            session.Entered = true;
        }
        catch
        {
            if (shell.GetParent() is null) shell.Free();
            throw;
        }
    }

    internal static void ResumeMapWhenBound()
    {
        var session = RavenReplayLoadHooks.Active;
        if (session is not { Entered: true } || session.Ready.IsCompleted || NMapScreen.Instance is not { } map
            || !ReferenceEquals(RavenServiceHooks.CurrentPlayer?.RunState, session.Restored)
            || !ReferenceEquals(session.Restored.CurrentRoom, session.Room)) return;
        try
        {
            if (!session.RequestedMap)
            {
                session.RequestedMap = true;
                map.Open(); // The stable map gate holds this request until all opportunities finish.
            }
            if (RavenServiceHooks.State.HasOpportunity)
            {
                if (!RavenMapGateHooks.HasDeferredMap || !RavenOverlayHooks.ReplayPanelMounted) return;
            }
            else if (!map.IsOpen && !(RavenMapGateHooks.HasDeferredMap && RavenOverlayHooks.ReplayPanelMounted)) return;
            session.SignalReady();
            Logger.Log("[Raven] Restored lamp source and map continuation without offering room loot again.");
        }
        catch (Exception error)
        {
            session.SignalFailure(error);
        }
    }

    internal static Control ContinueSurface(Action openMap)
    {
        var shell = new Control { Name = "RavenResolvedRoom", MouseFilter = Control.MouseFilterEnum.Ignore };
        shell.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var proceed = new Button { Text = "继续", Position = new Vector2(40, 160) };
        proceed.Pressed += openMap;
        shell.AddChild(proceed);
        return shell;
    }
}

internal static partial class RavenOverlayHooks
{
    internal static bool ReplayPanelMounted => _panel is { } panel && GodotObject.IsInstanceValid(panel)
        && panel.IsInsideTree() && panel.IsVisibleInTree() && !panel.IsQueuedForDeletion();
}

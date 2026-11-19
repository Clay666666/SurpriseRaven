using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SurpriseRaven.Hooks.Raven;

[HarmonyPatch]
internal static class RavenReplayRecoveryHooks
{
    private static readonly AsyncLocal<SerializableRun?> Retry = new();
    internal static bool RestoringNative => Retry.Value is not null;

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun)), HarmonyPrefix]
    private static bool KeepHostCheckpointDuringPickup(ref Task __result)
    {
        if (!RavenReplayLogHooks.ClaimsNativeCheckpoint()) return true;
        Logger.Log("[Raven] Preserved the native checkpoint during an unconfirmed lamp pickup.");
        __result = Task.CompletedTask;
        return false;
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.IncrementNumReloads)), HarmonyPrefix]
    private static bool CountOnlyOriginalLoad(SerializableRun save, ref Task __result)
    {
        if (!ReferenceEquals(Retry.Value, save)) return true;
        __result = Task.CompletedTask;
        return false;
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedSingleplayer)), HarmonyPostfix]
    private static void ObserveRunAdmission(RunState state, ref Task __result)
    {
        if (RavenReplayLoadHooks.Active is { } session && ReferenceEquals(state, session.Restored))
            __result = RecoverIfNeeded(__result, session, scene: false);
    }

    [HarmonyPatch(typeof(NGame), nameof(NGame.LoadRun)), HarmonyPostfix]
    private static void ObserveRoomLoad(RunState runState, ref Task __result)
    {
        if (RavenReplayLoadHooks.Active is { } session && ReferenceEquals(runState, session.Restored))
            __result = RecoverIfNeeded(__result, session, scene: true);
    }

    private static Task RecoverIfNeeded(Task original, RavenReplayLoadHooks.Session session, bool scene) =>
        CompleteOrRestoreNative(original, () => ReferenceEquals(RavenReplayLoadHooks.Active, session),
            () => !scene ? Task.CompletedTask : !session.Entered
                ? Task.FromException(new InvalidOperationException("The completed-room view was not installed."))
                : session.Ready.WaitAsync(TimeSpan.FromSeconds(8)),
            error => RestoreNative(session, scene, error));

    internal static async Task CompleteOrRestoreNative(Task admission, Func<bool> current,
        Func<Task> ready, Func<Exception, Task> fallback)
    {
        try
        {
            await admission;
            if (current()) await ready();
        }
        catch (Exception error) when (!current())
        {
            // Both Continue and QuickRestart clean up the global run when their task fails.
            Logger.Log($"[Raven] Retired lamp admission stopped: {error.Message}");
        }
        catch (Exception error) { await fallback(error); }
    }

    private static async Task RestoreNative(RavenReplayLoadHooks.Session session, bool scene, Exception error)
    {
        Logger.Log($"[Raven] Lamp restoration failed; returning to the native checkpoint: {error}");
        try { RavenReplayJournalHooks.Discard(session.Key); }
        catch (Exception io) { Logger.Log($"[Raven] Could not remove rejected journal: {io.Message}"); }
        Retry.Value = session.Host;
        try
        {
            RunManager.Instance.CleanUp();
            RavenReplayLoadHooks.AllowNativeRetry(session.Original);
            // The original host and its pre-finished room are untouched by projection.
            await RunManager.Instance.SetUpSavedSingleplayer(session.Original, session.Host);
            if (!ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), session.Original))
            {
                RavenReplayLoadHooks.Retire(session.Original);
                return;
            }
            if (scene)
            {
                var game = NGame.Instance ?? throw new InvalidOperationException("The game scene has closed.");
                await game.LoadRun(session.Original, session.Host.PreFinishedRoom);
            }
        }
        catch (Exception late) when (RunManager.Instance.DebugOnlyGetState() is { } live
            && !ReferenceEquals(live, session.Original))
        {
            RavenReplayLoadHooks.Retire(session.Original);
            Logger.Log($"[Raven] Retired native retry: {late.Message}");
        }
        finally { Retry.Value = null; }
    }
}

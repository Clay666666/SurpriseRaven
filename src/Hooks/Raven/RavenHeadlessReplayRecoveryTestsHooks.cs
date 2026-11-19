using Godot;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    // Exercise the same async boundary that wraps both native entry points, without starting a run.
    private static async Task<int> RunReplayRecoveryChecksAsync()
    {
        Require(DisplayServer.GetName() == "headless" && !RunManager.Instance.IsInProgress,
            "Replay recovery checks require an isolated idle game.");
        var tree = (SceneTree)Engine.GetMainLoop();
        async Task Frame() => await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        int checks = 0;
        void Check(bool condition, string message) { Require(condition, message); checks++; }

        // A restored model is not a playable room until its deferred view has attached.
        foreach (bool failAdmission in new[] { true, false })
        {
            var admission = Signal();
            var view = Signal();
            var viewStarted = Signal();
            var fallbackStarted = Signal();
            var native = Signal();
            var error = new InvalidOperationException(failAdmission ? "injected setup fault" : "injected deferred scene fault");
            int retries = 0;
            Exception? cause = null;
            var result = RavenReplayRecoveryHooks.CompleteOrRestoreNative(admission.Task, () => true,
                () => { viewStarted.SetResult(); return view.Task; },
                failure => { cause = failure; retries++; fallbackStarted.SetResult(); return native.Task; });
            await Frame();
            Check(!result.IsCompleted && retries == 0 && !viewStarted.Task.IsCompleted,
                "Pending replay admission waits without attempting a second load.");
            if (failAdmission) admission.SetException(error);
            else
            {
                admission.SetResult();
                await viewStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Frame();
                Check(!result.IsCompleted && retries == 0, "An installed run model still waits for the room view.");
                view.SetException(error);
            }
            await fallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Frame();
            Check(!result.IsCompleted && retries == 1 && ReferenceEquals(cause, error),
                "A deferred failure starts exactly one native fallback and waits for its completion.");
            if (failAdmission)
            {
                native.SetResult();
                await result;
                Check(result.IsCompletedSuccessfully && !viewStarted.Task.IsCompleted,
                    "Successful native admission ends this fallback without running the failed scene.");
            }
            else
            {
                var nativeError = new InvalidDataException("injected native load failure");
                native.SetException(nativeError);
                try { await result; }
                catch (InvalidDataException actual) when (ReferenceEquals(actual, nativeError)) { }
                Check(result.IsFaulted && retries == 1 && ReferenceEquals(result.Exception?.InnerException, nativeError),
                    "A second failure from the native load surfaces once without retrying the journal.");
            }
        }

        // Two old asynchronous operations fail after a replacement session has acquired the room.
        var oldAdmission = Signal();
        var oldView = Signal();
        var oldViewStarted = Signal();
        int owner = 1, staleRetries = 0, staleViews = 0;
        Task RetryStale(Exception _) { staleRetries++; return Task.CompletedTask; }
        var retiredSetup = RavenReplayRecoveryHooks.CompleteOrRestoreNative(oldAdmission.Task, () => owner == 1,
            () => { staleViews++; return Task.CompletedTask; }, RetryStale);
        var retiredView = RavenReplayRecoveryHooks.CompleteOrRestoreNative(Task.CompletedTask, () => owner == 1,
            () => { oldViewStarted.SetResult(); return oldView.Task; }, RetryStale);
        await oldViewStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Frame();
        owner = 2;
        oldAdmission.SetException(new InvalidOperationException("retired setup finished late"));
        oldView.SetException(new InvalidOperationException("retired view finished late"));
        await Task.WhenAll(retiredSetup, retiredView).WaitAsync(TimeSpan.FromSeconds(5));
        Check(retiredSetup.IsCompletedSuccessfully && retiredView.IsCompletedSuccessfully
            && owner == 2 && staleRetries == 0 && staleViews == 0,
            "Late setup and view failures cannot invoke either caller's global cleanup against the next run.");
        Check(!RunManager.Instance.IsInProgress, "The async recovery fixture did not enter a run.");
        Logger.Log($"[Raven] Headless replay recovery passed {checks} cross-frame checks.");
        return checks;
    }
}

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestExitCoordination()
    {
        foreach (bool canFade in new[] { false, true })
        {
            var gate = new RavenExitCoordinatorHooks();
            var completion = new TaskCompletionSource();
            var calls = new List<string>();
            var state = new RavenState
            {
                PendingLights = [2],
                Pending = new RavenPendingRound { Outcome = RavenOutcome.Swap, StakeConsumed = true }
            };
            bool masked = false;
            Task<bool> FadeOut() { calls.Add("out"); masked = canFade; return Task.FromResult(canFade); }
            Task FadeIn() { calls.Add("in"); masked = false; return Task.CompletedTask; }
            async Task Exit()
            {
                calls.Add("exit");
                await completion.Task;
                RavenTransactionHooks.Abandon(state);
            }
            var task = gate.RunAsync(FadeOut, FadeIn, Exit, () => calls.Add("settled"));
            Check(gate.IsRunning && !task.IsCompleted && masked == canFade,
                "Exit lock did not span delayed abandonment.");
            int before = calls.Count;
            Check(!gate.RunAsync(FadeOut, FadeIn, Exit, () => { }).GetAwaiter().GetResult()
                && calls.Count == before, "Duplicate exit repeated abandonment or its transition.");
            completion.SetResult();
            Check(task.GetAwaiter().GetResult() && !gate.IsRunning && !masked, "Exit did not release mask and lock.");
            Check(!state.HasOpportunity && state.Pending is null, "Exit retained the unclaimed output.");
            Check(calls.SequenceEqual(canFade ? ["out", "exit", "settled", "in"]
                : new[] { "out", "exit", "settled" }), "Abandonment transition order changed.");
        }

        var failedGate = new RavenExitCoordinatorHooks();
        int restores = 0;
        var failed = failedGate.RunAsync(() => Task.FromResult(true),
            () => { restores++; return Task.CompletedTask; },
            () => Task.FromException(new InvalidOperationException("fixture abandonment failure")),
            () => throw new InvalidOperationException("Failure must not report success."));
        Check(failed.IsFaulted && !failedGate.IsRunning && restores == 1, "Failed abandonment retained mask or exit lock.");
        Check(failedGate.RunAsync(() => Task.FromResult(false),
            () => throw new InvalidOperationException("No fade was started."),
            () => Task.CompletedTask, () => { }).GetAwaiter().GetResult(), "Failed exit could not be retried.");

        var fading = new RavenExitCoordinatorHooks();
        var failure = fading.RunAsync(() => Task.FromResult(true),
            () => Task.FromException(new InvalidOperationException("fixture fade-in failure")),
            () => Task.CompletedTask, () => { });
        Check(failure.IsFaulted && !fading.IsRunning, "Fade-in failure left exit locked.");
        var canceled = fading.RunAsync(() => Task.FromResult(false),
            () => Task.CompletedTask, () => Task.FromCanceled(new CancellationToken(true)), () => { });
        Check(canceled.IsCanceled && !fading.IsRunning, "Canceled abandonment left exit locked.");
        int prematureExits = 0;
        var fadeOutFailure = fading.RunAsync(() => Task.FromException<bool>(new InvalidOperationException("fixture fade-out failure")),
            () => Task.CompletedTask, () => { prematureExits++; return Task.CompletedTask; }, () => { });
        Check(fadeOutFailure.IsFaulted && !fading.IsRunning && prematureExits == 0,
            "Failed fade-out executed abandonment or retained the lock.");

        var finalFade = new TaskCompletionSource();
        var finishing = new RavenExitCoordinatorHooks();
        int exits = 0;
        Task ExitOnce() { exits++; return Task.CompletedTask; }
        var waiting = finishing.RunAsync(() => Task.FromResult(true), () => finalFade.Task, ExitOnce, () => { });
        Check(finishing.IsRunning && !waiting.IsCompleted, "Exit unlocked before final fade-in completed.");
        Check(!finishing.RunAsync(() => Task.FromResult(true), () => Task.CompletedTask, ExitOnce, () => { }).GetAwaiter().GetResult()
            && exits == 1, "A second exit entered during final fade-in.");
        finalFade.SetResult();
        Check(waiting.GetAwaiter().GetResult() && !finishing.IsRunning, "Final fade-in did not release exit lock.");
    }
}

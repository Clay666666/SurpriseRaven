using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestAcquisitionObserver()
    {
        var relic = ModelDb.Relic<Strawberry>();
        int notifications = 0, completions = 0;
        var scope = new RavenAcquisitionHooks.AcquisitionScope(() => completions++);
        var observed = RavenAcquisitionHooks.ObserveObtain(Task.FromResult<RelicModel>(relic),
            value => { Check(ReferenceEquals(value, relic), "Award identity changed."); notifications++; }, scope);
        Check(ReferenceEquals(observed.GetAwaiter().GetResult(), relic), "Observer replaced the award result.");
        scope.Complete();
        Check(notifications == 1 && completions == 1, "Success notification or completion was duplicated.");

        var expected = new InvalidOperationException("headless original failure fixture");
        Exception? actual = null;
        notifications = 0;
        completions = 0;
        scope = new RavenAcquisitionHooks.AcquisitionScope(() => completions++);
        try
        {
            RavenAcquisitionHooks.ObserveObtain(Task.FromException<RelicModel>(expected),
                _ => notifications++, scope).GetAwaiter().GetResult();
        }
        catch (Exception error) { actual = error; }
        scope.Complete();
        Check(ReferenceEquals(expected, actual), "Original asynchronous failure did not propagate unchanged.");
        Check(notifications == 0 && completions == 1, "Failed award notified or completed twice.");

        notifications = 0;
        completions = 0;
        actual = null;
        scope = new RavenAcquisitionHooks.AcquisitionScope(() => completions++);
        try
        {
            RavenAcquisitionHooks.ObserveObtain(
                Task.FromCanceled<RelicModel>(new CancellationToken(true)),
                _ => notifications++, scope).GetAwaiter().GetResult();
        }
        catch (Exception error) { actual = error; }
        Check(actual is OperationCanceledException && notifications == 0 && completions == 1,
            "Canceled acquisition was notified, completed twice, or changed cancellation semantics.");

        completions = 0;
        scope = new RavenAcquisitionHooks.AcquisitionScope(() => completions++);
        observed = RavenAcquisitionHooks.ObserveObtain(Task.FromResult<RelicModel>(relic),
            _ => throw new InvalidOperationException("expected headless subscriber failure fixture"), scope);
        Check(ReferenceEquals(observed.GetAwaiter().GetResult(), relic), "Subscriber failure broke original success.");
        Check(completions == 1, "Subscriber failure did not balance acquisition scope.");

        notifications = 0;
        completions = 0;
        scope = new RavenAcquisitionHooks.AcquisitionScope(() => completions++);
        var source = new TaskCompletionSource<RelicModel>();
        observed = RavenAcquisitionHooks.ObserveObtain(source.Task, _ => notifications++, scope);
        Check(!observed.IsCompleted && notifications == 0 && completions == 0,
            "Observer completed before the original award completed.");
        source.SetResult(relic);
        Check(ReferenceEquals(observed.GetAwaiter().GetResult(), relic)
            && notifications == 1 && completions == 1, "Deferred original completion was not observed once.");

        Check(RavenServiceHooks.CheckAcquisitionSuppressionForHeadless(relic),
            "Acquisition suppression/counter was not balanced.");
        Check(!RavenServiceHooks.SuppressAcquisitions, "Suppression leaked out of the headless fixture.");
    }
}

public static partial class RavenServiceHooks
{
    internal static bool CheckAcquisitionSuppressionForHeadless(RelicModel relic)
    {
        if (System.Environment.GetEnvironmentVariable("STS2_SURPRISE_RAVEN_HEADLESS_TEST") != "1"
            || Godot.DisplayServer.GetName() != "headless" || RunManager.Instance.IsInProgress)
            throw new InvalidOperationException("Acquisition fixture requires headless with no active run.");
        int baselineDepth = _acquisitionDepth;
        int baselineSuppression = SuppressionDepth.Value;
        var originalPlayer = CurrentPlayer;
        try
        {
            AcquisitionStarted();
            var scope = new RavenAcquisitionHooks.AcquisitionScope();
            if (_acquisitionDepth != baselineDepth + 1) return false;
            scope.Complete();
            scope.Complete();
            if (_acquisitionDepth != baselineDepth) return false;
            SuppressionDepth.Value++;
            if (!SuppressAcquisitions) return false;
            OnObtained(null!, relic); // The real observer entry must return before any run/player access.
            return ReferenceEquals(CurrentPlayer, originalPlayer);
        }
        finally
        {
            _acquisitionDepth = baselineDepth;
            SuppressionDepth.Value = baselineSuppression;
        }
    }
}

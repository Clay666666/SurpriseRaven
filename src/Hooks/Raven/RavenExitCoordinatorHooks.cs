namespace SurpriseRaven.Hooks.Raven;

// Owns abandonment and its transition, retaining the exit lock through the final fade-in.
// Delegates keep the ordering testable without starting a run or a graphical display.
internal sealed class RavenExitCoordinatorHooks
{
    internal bool IsRunning { get; private set; }

    internal async Task<bool> RunAsync(Func<Task<bool>> fadeOut, Func<Task> fadeIn,
        Func<Task> exit, Action settled)
    {
        if (IsRunning) return false;
        IsRunning = true;
        bool faded = false;
        try
        {
            faded = await fadeOut();
            await exit();
            settled();
            return true;
        }
        finally
        {
            try { if (faded) await fadeIn(); }
            finally { IsRunning = false; }
        }
    }
}

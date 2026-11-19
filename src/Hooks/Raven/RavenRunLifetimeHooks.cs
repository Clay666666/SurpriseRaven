using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

// QuickRestart can replace a player while a native reward selection still owns an incomplete Task.
// A late continuation keeps the locks of its own lifetime, even when the saved seed and floor match.
[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp), [typeof(bool)])]
internal sealed class RavenRunLifetimeHooks(Player? player, long generation)
{
    internal Player? Player { get; } = player;
    internal IRunState? Run { get; } = player?.RunState;
    internal long Generation { get; } = generation;
    internal int Acquisitions;
    internal readonly AsyncLocal<int> Suppression = new();
    internal bool Busy, ExitRequested;
    internal static bool CleaningUp { get; private set; }

    internal bool Owns(Player target) => ReferenceEquals(Player, target) && ReferenceEquals(Run, target.RunState);

    [HarmonyPrefix]
    private static void RavenCleanUpPrefix(out bool __state)
    {
        __state = CleaningUp;
        CleaningUp = true;
        RavenServiceHooks.ChangeRun(null);
    }

    [HarmonyFinalizer]
    private static void RavenCleanUpFinalizer(bool __state) => CleaningUp = __state;
}

public static partial class RavenServiceHooks
{
    private static RavenRunLifetimeHooks _lifetime = new(null, 0);
    internal static long Generation => _lifetime.Generation;
    public static Player? CurrentPlayer => _lifetime.Player;
    public static bool Busy => _lifetime.Busy;
    internal static bool HasPendingAcquisition => _lifetime.Acquisitions != 0;
    public static bool SuppressAcquisitions => _lifetime.Suppression.Value > 0;

    internal static void ChangeRun(Player? player)
    {
        var previous = _lifetime;
        if (player is not null && previous.Owns(player)) return;
        _lifetime = new RavenRunLifetimeHooks(player, previous.Generation + 1);
        RavenMapGateHooks.Reset();
        RavenOverlayHooks.Reset();
        RavenStageSession.TimelineActive = false;
        RavenStageSession.IntroOpportunityId = -1;
        _frameErrorLogged = false;
        if (previous.Player is not null)
            Logger.Log($"[Raven] Detached session {previous.Generation} (pickup={previous.Busy}, obtains={previous.Acquisitions}).");
        if (player is null) return;
        var state = RavenStorageHooks.Get(player);
        RavenStageSession.RestoreOpportunity(state);
        bool changed = RavenTransactionHooks.RecoverInterrupted(state);
        if (RavenSessionHooks.MigrateLegacy(state))
        {
            Logger.Log("[Raven] Migrated a legacy bonus choice into the locked round result.");
            changed = true;
        }
        if (changed) SaveAndNotify(player);
    }

    // The Obtain hook may run before Tick sees the replacement. Only the actual live player may adopt it.
    internal static bool BindLivePlayer(Player player)
    {
        var run = RunManager.Instance;
        if (!IsSingleplayer || run.IsCleaningUp || player is null
            || !ReferenceEquals(run.DebugOnlyGetState()?.Players.FirstOrDefault(), player)) return false;
        ChangeRun(player);
        return true;
    }

    internal static Action BeginObservedAcquisition()
    {
        var lifetime = _lifetime;
        lifetime.Acquisitions++;
        return () => lifetime.Acquisitions = Math.Max(0, lifetime.Acquisitions - 1);
    }

    // Preserve the existing paired public calls for callers that span an await.
    private sealed record AcquisitionFrame(Action Finish, AcquisitionFrame? Parent);
    private static readonly AsyncLocal<AcquisitionFrame?> AcquisitionFrames = new();
    public static void AcquisitionStarted() =>
        AcquisitionFrames.Value = new AcquisitionFrame(BeginObservedAcquisition(), AcquisitionFrames.Value);
    public static void AcquisitionFinished()
    {
        if (AcquisitionFrames.Value is not { } frame) return;
        AcquisitionFrames.Value = frame.Parent;
        frame.Finish();
    }

    private static void PersistCurrent(Player player)
    {
        if (_lifetime.Owns(player)) RavenStorageHooks.Touch(player);
    }
}

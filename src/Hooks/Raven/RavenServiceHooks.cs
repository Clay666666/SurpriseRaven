using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenServiceHooks
{
    private static bool _initialized;
    private static bool _frameErrorLogged;
    public static RavenState State => CurrentPlayer is { } player ? RavenStorageHooks.Get(player) : new();
    public static event Action? Changed;

    public static void Initialize()
    {
        if (_initialized) return;
        RavenStorageHooks.Initialize();
        RavenNativeAncientStorageHooks.Initialize();
        if (Engine.GetMainLoop() is not SceneTree tree) throw new InvalidOperationException("No SceneTree.");
        tree.ProcessFrame += Tick;
        _initialized = true;
        Logger.Log("[Raven] Singleplayer lamp service initialized.");
    }

    public static bool IsSingleplayer => !RavenRunLifetimeHooks.CleaningUp && RunManager.Instance.IsInProgress
        && RunManager.Instance.NetService?.Type == NetGameType.Singleplayer;
    public static string RoomKey(Player player) =>
        $"{player.RunState.CurrentActIndex}:{player.RunState.ActFloor}:{player.RunState.BaseRoom?.Id}";

    public static bool CanInteract(Player player)
    {
        var run = RunManager.Instance;
        var combat = CombatManager.Instance;
        return IsSingleplayer && _lifetime.Owns(player) && ReferenceEquals(run.DebugOnlyGetState(), player.RunState)
            && player.RunState.BaseRoom is not null && !Busy
            && !run.IsCleaningUp && !run.IsGameOver && !HasPendingAcquisition
            && !combat.IsInProgress && !combat.IsStarting && !combat.IsEnding
            && run.ActionExecutor?.IsRunning != true && run.ActionQueueSet?.IsEmpty != false;
    }

    public static void OnObtained(Player player, RelicModel relic) => OnObtained(player, relic, false);

    internal static void OnObtained(Player player, RelicModel relic, bool isDirectNeowAncient)
    {
        if (isDirectNeowAncient)
        {
            Logger.Log($"[Raven] No opportunity: direct Neow ancient gift {relic.Id}.");
            return;
        }
        if (!BindLivePlayer(player) || SuppressAcquisitions || RavenRulesHooks.IsExcluded(relic)
            || !RavenRulesHooks.GrantsOpportunity(relic.Rarity)) return;
        var state = RavenStorageHooks.Get(player);
        RavenOpportunityQueueHooks.Enqueue(state, RavenStorageHooks.Rng(player, "initial").NextInt(0, 6), RoomKey(player));
        SaveAndNotify(player);
        Logger.Log($"[Raven] Opportunity: {relic.Id}, count={state.PendingLights.Count}, lights={state.Lights}.");
    }

    public static IReadOnlyList<RelicModel> GetStakes(Player player) => player.Relics
        .Where(RavenStakeEligibilityHooks.CanStake).ToList();

    private static void Tick()
    {
        try
        {
            if (!IsSingleplayer || RunManager.Instance.IsCleaningUp)
            {
                if (CurrentPlayer is not null) ChangeRun(null);
                return;
            }
            var player = RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault();
            if (player is null) return;
            ChangeRun(player);
            // Saved players become visible before LoadIntoLatestMapCoord has restored their base room.
            if (player.RunState.BaseRoom is null) return;
            var state = RavenStorageHooks.Get(player);
            if (!Busy && ReconcileOriginal(player, state)) SaveAndNotify(player);
            if (!Busy && !state.Faulted && state.HasOpportunity && state.RoomKey != RoomKey(player))
            {
                _ = RavenOverlayHooks.RunServiceOperation(() => ExpireOpportunitiesAsync(player), !RavenOverlayHooks.HasOpenPanel);
                return;
            }
            if (!Busy && RavenOpportunityQueueHooks.Flush(state, RoomKey(player))) SaveAndNotify(player);
            if (state.Pending is { } pending && !RavenTransactionHooks.IsReady(pending)
                && CanStart(player, state, state.Revision))
            {
                _ = RavenOverlayHooks.RunServiceOperation(() => NormalizePendingAsync(player), !RavenOverlayHooks.HasOpenPanel);
                return;
            }
            // A consumed swallow resolves once its animation is over, or immediately after restore.
            if (!Busy && state.Pending?.Outcome == RavenOutcome.Swallow && !state.Faulted
                && !RavenStageSession.TimelineActive && CanInteract(player))
                _ = RavenOverlayHooks.RunServiceOperation(() => ResolveSwallowAsync(player), !RavenOverlayHooks.HasOpenPanel);
            RavenOverlayHooks.TryShow();
        }
        catch (Exception error)
        {
            if (!_frameErrorLogged) Logger.Log($"[Raven] UI/lifecycle error: {error}");
            _frameErrorLogged = true;
        }
    }

    private static void SaveAndNotify(Player player,
        [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        if (!_lifetime.Owns(player)) return;
        RavenStorageHooks.Touch(player);
        RavenReplayLogHooks.Mark(player, operation);
        foreach (var callback in Changed?.GetInvocationList() ?? [])
            try { ((Action)callback)(); }
            catch (Exception error) { Logger.Log($"[Raven] Subscriber failed: {error}"); }
    }
}

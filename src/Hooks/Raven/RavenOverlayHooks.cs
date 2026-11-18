using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace SurpriseRaven.Hooks.Raven;

// Native overlay stack owns modal focus/backstop; this class owns node lifetime and the
// game's own room transition around entering and leaving the lamp.
internal static partial class RavenOverlayHooks
{
    private static RavenLampPanelHooks? _panel;
    private static bool _subscribed, _entering, _inPanelOperation;
    private static readonly RavenExitCoordinatorHooks ExitCoordinator = new();
    private static bool _leaving => ExitCoordinator.IsRunning;
    private static string? _resultMessage;
    internal static bool HasOpenPanel => _entering || (_panel is not null && GodotObject.IsInstanceValid(_panel));
    internal static bool InPanelOperation => _inPanelOperation;

    internal static void TryShow()
    {
        if (!_subscribed)
        {
            RavenServiceHooks.Changed += Refresh;
            _subscribed = true;
        }
        var player = RavenServiceHooks.CurrentPlayer;
        if (player is null || (RavenServiceHooks.Busy && !_inPanelOperation)
            || (!RavenServiceHooks.State.HasOpportunity && _resultMessage is null))
        {
            if (!_inPanelOperation && !_leaving) Dismiss();
            if (player is not null && !RavenServiceHooks.Busy && !_leaving) RavenMapGateHooks.TryResume();
            return;
        }
        if (HasOpenPanel || _leaving || !CanOpen(player)) return;
        if (RavenTransitionHooks.CanTransition) _ = EnterAsync(player);
        else Push(player);
    }

    private static bool CanOpen(Player player) => RavenServiceHooks.CanInteract(player)
        && NOverlayStack.Instance is { } stack && CanCoverStack(stack)
        && NModalContainer.Instance?.OpenModal is null && NMapScreen.Instance?.IsOpen != true
        && NCapstoneContainer.Instance?.InUse != true;

    private static async Task EnterAsync(Player player)
    {
        _entering = true;
        bool faded = false;
        try
        {
            faded = await RavenTransitionHooks.FadeOutAsync();
            if (CanOpen(player) && (RavenServiceHooks.State.HasOpportunity || _resultMessage is not null)) Push(player);
        }
        catch (Exception error) { Logger.Log($"[Raven] Enter transition failed: {error}"); }
        finally
        {
            _entering = false;
            if (faded) await RavenTransitionHooks.FadeInAsync();
        }
    }

    private static void Push(Player player)
    {
        if (NOverlayStack.Instance is not { } stack) return;
        _panel = new RavenLampPanelHooks(player, RavenServiceHooks.State.HasOpportunity ? null : _resultMessage);
        stack.Push(_panel);
        Logger.Log("[Raven] Lamp overlay opened.");
    }

    // Terminal reward screens remain in the stack even after every reward is claimed.
    // A deferred map request means the player explicitly pressed Proceed/Map instead.
    internal static bool CanCoverStack(NOverlayStack stack) => stack.ScreenCount == 0
        || stack.Peek() is NRewardsScreen rewards && (rewards.IsComplete || RavenMapGateHooks.HasDeferredMap);

    internal static void Dismiss()
    {
        var panel = _panel;
        _panel = null;
        if (panel is null || !GodotObject.IsInstanceValid(panel)) return;
        if (NOverlayStack.Instance is { } stack) stack.Remove(panel);
        else panel.AfterOverlayClosed();
    }

    internal static void Closed(RavenLampPanelHooks panel)
    {
        if (ReferenceEquals(panel, _panel)) _panel = null;
        if (!RavenServiceHooks.State.HasOpportunity) _resultMessage = null;
    }

    internal static void ShowResult(string message)
    {
        if (!string.IsNullOrWhiteSpace(message)) _resultMessage = message;
        TryShow();
    }

    // Keep the lamp instance during pickup. Native selection screens cover it via the overlay stack.
    internal static async Task RunServiceOperation(Func<Task> operation, bool dismiss = false)
    {
        if (RavenServiceHooks.Busy || _inPanelOperation) return;
        _inPanelOperation = !dismiss;
        if (dismiss) Dismiss();
        try { await operation(); }
        catch (Exception error) { Logger.Log($"[Raven] Lamp operation failed: {error}"); }
        finally
        {
            _inPanelOperation = false;
            if (!RavenServiceHooks.State.HasOpportunity) _resultMessage = RavenServiceHooks.State.LastMessage;
            if (_panel is not null && GodotObject.IsInstanceValid(_panel)) _panel.Refresh();
            else TryShow();
        }
    }

    internal static async void ExitWithTransition(Func<Task> exit)
    {
        if (_leaving) return;
        try
        {
            await ExitCoordinator.RunAsync(RavenTransitionHooks.FadeOutAsync,
                RavenTransitionHooks.FadeInAsync, exit, CompleteExit);
        }
        catch (Exception error) { Logger.Log($"[Raven] Exit transition failed: {error}"); }
        finally { TryShow(); }
    }

    private static void CompleteExit()
    {
        if (RavenServiceHooks.State.HasOpportunity && _panel is not null && GodotObject.IsInstanceValid(_panel)
            && !_panel.IsTerminal) _panel.Refresh();
        else
        {
            _resultMessage = null;
            Dismiss();
        }
    }

    private static void Refresh()
    {
        bool idle = !RavenServiceHooks.State.HasOpportunity && _resultMessage is null;
        if (!_inPanelOperation && !_leaving && (RavenServiceHooks.Busy || idle))
        {
            Dismiss();
            return;
        }
        if (_panel is not null && GodotObject.IsInstanceValid(_panel)) _panel.Refresh();
        else TryShow();
    }
}

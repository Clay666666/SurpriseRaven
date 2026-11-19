using Godot;
using static SurpriseRaven.Hooks.Raven.RavenStyleHooks;

namespace SurpriseRaven.Hooks.Raven;

// Reveal, pickup, result wording and audio selection for a round.
internal sealed partial class RavenStageHooks
{
    private void Reveal(RavenRoundPlan plan)
    {
        PlayResultAudio(plan.DisplayOutcome);
        var (title, color) = PopText(plan);
        Hud.Pop(title, color, animate: !ReducedMotion);
        Hud.SetMessage(ResultMessage(plan));
        if (plan.Outcome is RavenOutcome.Upgrade or RavenOutcome.Bonus)
            RavenFxHooks.Confetti(_fx, RavenLayout.Altar + new Vector2(0, -40), particles: !ReducedMotion);
        Hud.SetInfo(RavenServiceHooks.State.Round, plan.LightsAfter, false);
    }

    internal void PickupHovered()
    {
        _rewards.PickupHovered();
    }

    private void Pickup(RavenPickupRequest request)
    {
        if (!_canInteract() || _phase != RavenStagePhase.Pickup || IsBusy || RavenServiceHooks.Busy || _plan is null) return;
        var reward = request.Kind == RavenPickupKind.Original ? _plan.OriginalReturn : _plan.Rewards.ElementAtOrDefault(request.CandidateIndex);
        if (reward is null) return;
        _phase = RavenStagePhase.Collecting;
        _rewards.Interactive = false;
        _rewardsShown = false; // Rebuild remaining candidates if eligibility changes during pickup.
        long revision = RavenServiceHooks.State.Revision;
        _ = RavenOverlayHooks.RunServiceOperation(() => RavenServiceHooks.CollectAsync(_player, request, revision));
    }

    internal void PlayResultAudio(RavenOutcome outcome)
    {
        var eventName = outcome switch
        {
            RavenOutcome.Upgrade => "result.upgrade",
            RavenOutcome.Downgrade => "result.downgrade",
            RavenOutcome.Bonus => "result.bonus",
            RavenOutcome.Swallow => "result.swallow",
            _ => null
        };
        if (eventName is not null) _playAudio(eventName);
        if (outcome == RavenOutcome.Upgrade) _playAudio("result.upgrade_sfx");
    }

    internal static (string, Color) PopText(RavenRoundPlan plan) => plan.DisplayOutcome switch
    {
        RavenOutcome.Upgrade => ("升值成功", Gold),
        RavenOutcome.Swap => ("等价互换", new Color(0.65f, 0.82f, 0.95f)),
        RavenOutcome.Downgrade => ("物品降档", new Color(0.75f, 0.68f, 0.9f)),
        RavenOutcome.Bonus => ("奖励三选一!!!", new Color(0.75f, 0.68f, 0.9f)),
        _ => ("原件返还", Cream)
    };

    // One primary action per state; the secondary (leaving) states its consequence.
    internal static string PickupCaption(RavenRoundPlan plan)
    {
        string action = plan switch
        {
            { Outcome: RavenOutcome.Bonus, OriginalReturn: not null, Rewards.Count: 0 } => "取回原件",
            { Outcome: RavenOutcome.Bonus, OriginalReturn: not null } => "取回原件，并任选一件奖励",
            { Outcome: RavenOutcome.Bonus } => "任选一件奖励",
            _ => "点击或按 F 领取遗物"
        };
        return action + " · 离开将放弃未领取的遗物";
    }

    internal static string ResultMessage(RavenRoundPlan plan) => plan.Notice
        ?? (plan.OriginalState == RavenOriginalState.Missing ? "原遗物已被其他效果移除。" : "");
}

using Godot;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using static SurpriseRaven.Hooks.Raven.RavenStyleHooks;

namespace SurpriseRaven.Hooks.Raven;

internal enum RavenStagePhase { Ready, Running, Pickup, Collecting, Ended }

internal sealed partial class RavenStageHooks
{
    private RavenStagePhase _phase = RavenStagePhase.Ready;
    private RavenTimeline? _timeline;
    private RavenRoundPlan? _plan;
    private bool _planning, _rewardsShown;

    internal RavenStagePhase Phase => _phase;
    internal bool IsBusy => _planning || _timeline is not null || _phase == RavenStagePhase.Collecting;

    internal void Sync()
    {
        if (_planning || _timeline is not null) return;
        var state = RavenServiceHooks.State;
        if (_terminal is not null || !state.HasOpportunity || state.Faulted) EnterEnded(_terminal ?? state.LastMessage);
        else if (state.Pending is not null && RavenServiceHooks.GetPendingPlan(_player) is { } plan) EnterPickup(plan);
        else EnterReady(state);
    }

    internal void EnterReady(RavenState state)
    {
        _phase = RavenStagePhase.Ready;
        _plan = null;
        _rewardsShown = false;
        _rewards.Clear();
        bool intro = RavenStageSession.IntroOpportunityId != state.OpportunityId;
        if (intro)
        {
            RavenStageSession.IntroOpportunityId = state.OpportunityId;
            RavenBirdCuesHooks.Intro(_bird.Motion);
        }
        else if (!_bird.Flying) _bird.Perch();
        SetCandles(state.Lights);
        Hud.SetInfo(state.Round, state.Lights, false);
        Hud.SetMessage(state.LastMessage);
        Hud.SetCaption("");
    }

    internal void EnterPickup(RavenRoundPlan plan)
    {
        _phase = RavenStagePhase.Pickup;
        bool changed = !ReferenceEquals(_plan, plan);
        _plan = plan;
        if (!_bird.Flying) _bird.Perch();
        if (!_rewardsShown || changed) _rewards.ShowRewards(plan.Rewards, null, plan.OriginalReturn, plan.Outcome == RavenOutcome.Bonus);
        _rewardsShown = true;
        _rewards.Interactive = true;
        SetCandles(plan.LightsAfter);
        Hud.SetInfo(RavenServiceHooks.State.Round, plan.LightsAfter, false);
        Hud.SetMessage(plan.Notice ?? ResultMessage(plan));
        Hud.SetCaption(PickupCaption(plan));
        if (IsInsideTree() && IsVisibleInTree() && _canInteract())
            RavenFocusHooks.FocusDefault(this, DefaultFocus);
    }

    internal void EnterEnded(string message)
    {
        _phase = RavenStagePhase.Ended;
        _rewards.Clear();
        if (!_bird.Flying) _bird.Away();
        ExtinguishAll();
        Hud.SetInfo(0, 0, true);
        Hud.SetMessage(message);
        Hud.SetCaption("Esc 离开");
    }

    internal void Stake(RelicModel relic, Vector2 viewportPoint, long revision)
    {
        if (_phase != RavenStagePhase.Ready || IsBusy || RavenServiceHooks.Busy) return;
        _planning = true;
        RavenStageSession.TimelineActive = true; // Hold swallow resolution across asynchronous submission.
        _ = RavenOverlayHooks.RunServiceOperation(async () =>
        {
            try
            {
                _plan = await RavenServiceHooks.SubmitStakeAsync(_player, relic, revision);
                if (_generation == RavenServiceHooks.Generation && GodotObject.IsInstanceValid(this) && IsInsideTree())
                    BeginRound(relic, CanvasPoint(viewportPoint));
            }
            finally
            {
                _planning = false;
                if (_generation == RavenServiceHooks.Generation && _timeline is null) RavenStageSession.TimelineActive = false;
            }
        });
    }

    private void BeginRound(RelicModel relic, Vector2 from)
    {
        if (_plan is null)
        {
            Hud.Toast("渡鸦现在不收这件遗物。");
            return;
        }
        _phase = RavenStagePhase.Running;
        RavenStageSession.TimelineActive = true;
        _timeline = new RavenTimeline(RavenFrontendConfigHooks.Current.Timings, _plan.Outcome == RavenOutcome.Swallow,
            _plan.LightsAfter > _plan.LightsBefore, _plan.DisplayOutcome == RavenOutcome.Swap);
        _rewards.Interactive = false;
        _rewards.ShowStake(relic.Icon, from, RavenFrontendConfigHooks.Current.Timings.OfferFly);
        Hud.SetMessage(RavenServiceHooks.State.LastMessage);
        Hud.SetCaption("渡鸦正在路上……");
    }

    internal void OnCue(RavenCue cue, RavenRoundPlan plan)
    {
        RavenBirdCuesHooks.Apply(_bird.Motion, cue, RavenFrontendConfigHooks.Current.Timings);
        switch (cue)
        {
            case RavenCue.Takeoff:
                if (!ReducedMotion) RavenFxHooks.Feathers(_fx, RavenLayout.Perch + new Vector2(0, -20));
                break;
            case RavenCue.Grab:
                _rewards.HideStake();
                _bird.Carry(plan.Stake?.Icon);
                break;
            case RavenCue.Gone:
                _bird.Carry(null);
                break;
            case RavenCue.Ignite:
                IgniteEarnedCandles(plan);
                break;
            case RavenCue.ReturnStart:
                _bird.Carry(plan.Rewards.FirstOrDefault()?.Icon);
                break;
            case RavenCue.SwapSound:
                _playAudio("reward.swap");
                break;
            case RavenCue.Drop:
                _bird.Carry(null);
                if (ReferenceEquals(RavenServiceHooks.CurrentPlayer, _player)
                    && RavenServiceHooks.GetPendingPlan(_player) is null) break;
                _rewards.ShowRewards(plan.Rewards, _bird.BeakPosition, plan.OriginalReturn, plan.Outcome == RavenOutcome.Bonus,
                    reducedMotion: ReducedMotion);
                _rewardsShown = true;
                break;
            case RavenCue.Reveal:
                if (plan.Outcome != RavenOutcome.Swallow && ReferenceEquals(RavenServiceHooks.CurrentPlayer, _player)
                    && RavenServiceHooks.GetPendingPlan(_player) is null) break;
                Reveal(plan);
                break;
            case RavenCue.Landed:
                if (!ReducedMotion) RavenFxHooks.Feathers(_fx, RavenLayout.Perch + new Vector2(0, -20), 6);
                break;
            case RavenCue.Extinguish:
                ExtinguishAll(playSound: true);
                break;
            case RavenCue.SwallowVoice:
                PlayResultAudio(RavenOutcome.Swallow);
                Hud.Pop("渡鸦带走了物品", new Color(0.95f, 0.45f, 0.4f), animate: !ReducedMotion);
                Hud.SetMessage($"渡鸦带走了 {plan.Stake?.Title.GetFormattedText()}……");
                break;
            case RavenCue.Finish:
                _timeline = null;
                RavenStageSession.TimelineActive = false;
                if (plan.Outcome == RavenOutcome.Swallow) _phase = RavenStagePhase.Collecting; // Service resolves next tick.
                else Sync(); // Reconcile an original/candidate changed by another effect during the animation.
                RavenFocusHooks.FocusDefault(this, DefaultFocus);
                break;
        }
    }
}

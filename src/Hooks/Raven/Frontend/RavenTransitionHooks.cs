using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace SurpriseRaven.Hooks.Raven;

// Reuses the game's own room transition (NTransition.RoomFadeOut/RoomFadeIn), so entering and
// leaving the lamp honours fast mode exactly like entering a map room. Calls are always paired.
internal static class RavenTransitionHooks
{
    internal static bool CanTransition => DisplayServer.GetName() != "headless"
        && NGame.Instance?.Transition is { } transition && !transition.InTransition;

    internal static async Task<bool> FadeOutAsync()
    {
        if (!CanTransition) return false;
        await NGame.Instance!.Transition.RoomFadeOut();
        return true;
    }

    internal static async Task FadeInAsync()
    {
        if (NGame.Instance?.Transition is { } transition) await transition.RoomFadeIn();
    }
}

internal static class RavenStageSession
{
    // True while a round animation runs; a locked swallow waits for it before resolving.
    internal static bool TimelineActive { get; set; }
    internal static long IntroOpportunityId { get; set; } = -1;

    // A saved opportunity skips the raven's introductory flight.
    internal static void RestoreOpportunity(RavenState state) =>
        IntroOpportunityId = state.HasOpportunity ? state.OpportunityId : -1;
}

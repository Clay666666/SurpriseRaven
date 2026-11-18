namespace SurpriseRaven.Hooks.Raven;

// A new acquisition must not clear an old room's unclaimed original before forfeiture finishes.
internal static class RavenOpportunityQueueHooks
{
    internal static void Enqueue(RavenState state, int lights, string roomKey)
    {
        if (lights is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(lights));
        if (state.DeferredOpportunities.Count > 0 || state.HasOpportunity && state.RoomKey != roomKey)
        {
            state.DeferredOpportunities.Add(new RavenDeferredOpportunity(lights, roomKey));
            state.Revision++;
        }
        else RavenSessionHooks.Enqueue(state, lights, roomKey);
    }

    internal static bool Flush(RavenState state, string roomKey)
    {
        if (state.HasOpportunity || state.InFlight || state.Faulted || state.DeferredOpportunities.Count == 0) return false;
        var queued = state.DeferredOpportunities.ToArray();
        state.DeferredOpportunities.Clear();
        foreach (var offer in queued)
            if (offer.RoomKey == roomKey) RavenSessionHooks.Enqueue(state, offer.Lights, roomKey);
        state.Revision++;
        return true;
    }
}

using MegaCrit.Sts2.Core.Random;

namespace SurpriseRaven.Hooks.Raven;

internal static class RavenShuffleHooks
{
    // Preserve 0.111.0 Rng.Shuffle's order and consumption of the saved raven.reward stream.
    // The 0.113 UnstableShuffle extension creates a child RNG and changes that consumption.
    internal static void Shuffle<T>(IList<T> items, Rng rng)
    {
        for (int index = items.Count - 1; index > 0; index--)
        {
            int other = rng.NextInt(index + 1);
            (items[index], items[other]) = (items[other], items[index]);
        }
    }
}

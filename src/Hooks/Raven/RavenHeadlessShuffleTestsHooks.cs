using System.Text.Json;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestLegacyShuffle()
    {
        // Independently generated from the ILSpy-verified 111 generator/Shuffle algorithm.
        // Reproduce with tools/api-compat/legacy-shuffle-vectors.py; never derive from the Mod shuffle.
        (ulong Seed, int Count, int[] Order, ulong Next, int[] ContinuedOrder, ulong ContinuedNext)[] samples =
        [
            (0, 0, [], 11091344671253066420UL, [4, 2, 1, 0, 3], 18442103541295991498UL),
            (0, 1, [0], 11091344671253066420UL, [4, 2, 1, 0, 3], 18442103541295991498UL),
            (918273, 8, [4, 1, 0, 5, 6, 3, 7, 2], 7845565889868060961UL,
                [2, 3, 4, 1, 0], 17058573337288519524UL),
            (ulong.MaxValue, 16, [2, 10, 12, 13, 0, 1, 4, 5, 14, 3, 15, 6, 9, 7, 11, 8],
                14324370401275619504UL, [3, 4, 1, 2, 0], 997770897655940919UL)
        ];

        foreach (var sample in samples)
        {
            var rng = new Rng(sample.Seed);
            var items = Enumerable.Range(0, sample.Count).ToList();
            RavenShuffleHooks.Shuffle(items, rng);
            Check(items.SequenceEqual(sample.Order), $"111 shuffle permutation changed for {sample.Count} items.");
            int draws = Math.Max(0, sample.Count - 1);
            var saved = rng.ToSerializable();
            Check(saved.counter == draws, "Shuffle changed reward RNG consumption, including empty/single lists.");
            var json = JsonSerializer.Serialize(saved, SaveOptions);
            var restoredSave = JsonSerializer.Deserialize<SerializableRng>(json, SaveOptions)
                ?? throw new InvalidOperationException("Shuffled RNG snapshot did not restore.");
            var restored = new Rng(restoredSave);
            Check(rng.NextUnsignedLong() == sample.Next, "Shuffle changed the next reward random value.");
            Check(restored.NextUnsignedLong() == sample.Next, "JSON restore changed the next reward random value.");
            var continuation = Enumerable.Range(0, 5).ToList();
            var restoredContinuation = Enumerable.Range(0, 5).ToList();
            RavenShuffleHooks.Shuffle(continuation, rng);
            RavenShuffleHooks.Shuffle(restoredContinuation, restored);
            Check(continuation.SequenceEqual(sample.ContinuedOrder), "Continuous reward shuffle changed.");
            Check(restoredContinuation.SequenceEqual(sample.ContinuedOrder), "Restored reward shuffle changed.");
            Check(rng.NextUnsignedLong() == sample.ContinuedNext, "Continuous reward stream diverged after another shuffle.");
            Check(restored.NextUnsignedLong() == sample.ContinuedNext, "Restored reward stream diverged after another shuffle.");
            Check(rng.ToSerializable().counter == draws + 6 && restored.ToSerializable().counter == draws + 6,
                "Reward RNG counter diverged after save, restore and continued shuffling.");
        }
    }
}

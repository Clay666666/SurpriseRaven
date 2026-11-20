using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Random;

namespace SurpriseRaven.Hooks.Raven;

internal static class RavenGameplayOddsHooks
{
    private const string ResourceName = "SurpriseRaven.Config.gameplay-odds.json";
    private static readonly Lazy<RavenGameplayOdds> Published = new(() => RavenGameplayOddsConfigHooks.Parse(ReadPublishedJson()));
    private static RavenGameplayOdds Current => Published.Value;

    internal static int[] Row(RelicRarity tier, int lights) => Current.Outcomes[tier.ToString()][Math.Clamp(lights, 0, 6)];
    internal static int LightUpPercent => Current.LightUpPercent;
    internal static int InitialLights(Rng rng) => Select(Current.InitialLights, rng.NextInt(0, Current.InitialLights.Sum()));

    internal static int Select(int[] weights, int roll)
    {
        int index = 0;
        while (index < weights.Length - 1 && roll >= weights[index]) roll -= weights[index++];
        return index;
    }

    internal static string ReadPublishedJson()
    {
        using var stream = typeof(RavenGameplayOddsHooks).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"Missing embedded odds resource {ResourceName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static void Initialize()
    {
        Logger.Log($"[Raven] Packaged gameplay odds loaded: lightUpPercent={LightUpPercent}.");
    }
}

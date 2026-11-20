using System.Globalization;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace SurpriseRaven.Hooks.Raven;

public static class RavenRulesHooks
{
    // Explicit compatibility registry: the game has no Quest flag for relics.
    private static readonly HashSet<string> ExcludedIds = new(StringComparer.Ordinal);

    public static void RegisterExcludedRelic(string modelId) => ExcludedIds.Add(modelId);
    public static bool IsExcluded(RelicModel relic) =>
        relic.Rarity == RelicRarity.Event || ExcludedIds.Contains(relic.Id.ToString());
    public static bool GrantsOpportunity(RelicRarity rarity) => rarity is
        RelicRarity.Uncommon or RelicRarity.Rare or RelicRarity.Ancient or RelicRarity.Shop;
    public static int[] GetOdds(int lights) => GetWeightRow(lights, RelicRarity.Uncommon)[..5];

    public static int[] GetWeights(int lights, RelicRarity rarity) => (int[])GetWeightRow(lights, rarity).Clone();

    private static int[] GetWeightRow(int lights, RelicRarity rarity) => RavenGameplayOddsHooks.Row(Tier(rarity), lights);

    public static RelicRarity Tier(RelicRarity rarity) => rarity switch
    {
        RelicRarity.Ancient => RelicRarity.Ancient,
        RelicRarity.Rare => RelicRarity.Rare,
        RelicRarity.Uncommon or RelicRarity.Shop => RelicRarity.Uncommon,
        _ => RelicRarity.Common
    };

    public static RavenOutcome Roll(int lights, int roll, bool ancient) =>
        Roll(lights, roll, ancient ? RelicRarity.Ancient : RelicRarity.Uncommon);

    public static RavenOutcome Roll(int lights, int roll, RelicRarity rarity)
    {
        var weights = GetWeightRow(lights, rarity);
        if (roll < 0 || roll >= weights.Sum()) throw new ArgumentOutOfRangeException(nameof(roll));
        return SelectOutcome(weights, roll);
    }

    internal static RavenOutcome Roll(int lights, Rng rng, RelicRarity rarity) => Roll(lights, rng, rarity, out _);

    internal static RavenOutcome Roll(int lights, Rng rng, RelicRarity rarity, out int roll)
    {
        var weights = GetWeightRow(lights, rarity);
        roll = rng.NextInt(0, weights.Sum());
        return SelectOutcome(weights, roll);
    }

    private static RavenOutcome SelectOutcome(int[] weights, int roll) => (RavenOutcome)RavenGameplayOddsHooks.Select(weights, roll);

    public static RelicRarity Target(RelicRarity rarity, RavenOutcome outcome)
    {
        var tier = Tier(rarity);
        return outcome switch
        {
            RavenOutcome.Upgrade => tier switch
            {
                RelicRarity.Common => RelicRarity.Uncommon,
                RelicRarity.Uncommon => RelicRarity.Rare,
                _ => RelicRarity.Ancient
            },
            RavenOutcome.Downgrade => tier switch
            {
                RelicRarity.Ancient => RelicRarity.Rare,
                RelicRarity.Rare => RelicRarity.Uncommon,
                _ => RelicRarity.Common
            },
            _ => tier
        };
    }

    public static int NextLights(int lights, int roll)
    {
        if (roll is < 0 or >= 100) throw new ArgumentOutOfRangeException(nameof(roll));
        return Math.Clamp(lights + (roll < RavenGameplayOddsHooks.LightUpPercent ? 1 : 0), 0, 6);
    }

    public static string DescribeOdds(int lights) => DescribeOdds(lights, RelicRarity.Uncommon);

    public static string DescribeOdds(int lights, RelicRarity rarity)
    {
        var weights = GetWeightRow(lights, rarity);
        var total = weights.Sum();
        string[] labels = ["吞噬", "升档", "平换", Tier(rarity) == RelicRarity.Ancient ? "降为稀有" : "降档",
            "三选一", "保留原件"];
        var outcomes = Enumerable.Range(0, weights.Length).Where(i => weights[i] > 0)
            .Select(i => $"{labels[i]} {(100m * weights[i] / total).ToString("0.##", CultureInfo.InvariantCulture)}%");
        return string.Join(" · ", outcomes)
            + $"\n每轮未吞噬时有{RavenGameplayOddsHooks.LightUpPercent}%概率亮一灯，满灯仍可继续。";
    }
}

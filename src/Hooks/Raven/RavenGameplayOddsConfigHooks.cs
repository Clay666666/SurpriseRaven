using System.Text.Json;
using System.Text.Json.Serialization;

namespace SurpriseRaven.Hooks.Raven;

internal sealed class RavenGameplayOdds
{
    [JsonRequired] public Dictionary<string, int[][]> Outcomes { get; set; } = new(StringComparer.Ordinal);
    [JsonRequired] public int[] InitialLights { get; set; } = [];
    [JsonRequired] public int LightUpPercent { get; set; }
}

internal static class RavenGameplayOddsConfigHooks
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly (string Tier, int First, int Last)[] SupportedOutcomes =
    [
        ("Common", 0, 2), ("Uncommon", 0, 3), ("Rare", 0, 4), ("Ancient", 3, 5)
    ];

    internal static RavenGameplayOdds Parse(string content)
    {
        var odds = JsonSerializer.Deserialize<RavenGameplayOdds>(content, Json) ?? throw new InvalidDataException("Root is null.");
        if (odds.Outcomes is null || odds.Outcomes.Count != SupportedOutcomes.Length)
            throw new InvalidDataException("outcomes 必须包含 Common、Uncommon、Rare、Ancient 四档。");
        foreach (var (tier, first, last) in SupportedOutcomes)
        {
            if (!odds.Outcomes.TryGetValue(tier, out var rows) || rows is null || rows.Length != 7)
                throw new InvalidDataException($"outcomes.{tier} 必须有 0–6 灯共七行。");
            for (int lights = 0; lights < rows.Length; lights++)
            {
                CheckWeights(rows[lights], $"outcomes.{tier}[{lights}]");
                for (int index = 0; index < rows[lights].Length; index++)
                    if (rows[lights][index] != 0 && (index < first || index > last))
                        throw new InvalidDataException($"outcomes.{tier}[{lights}][{index}] 此结果在该档位必须为 0。");
            }
        }
        CheckWeights(odds.InitialLights, "initialLights");
        if (odds.LightUpPercent is < 0 or > 100)
            throw new InvalidDataException("lightUpPercent 必须是 0–100 的整数。");
        return odds;
    }

    private static void CheckWeights(int[]? weights, string path)
    {
        if (weights is not { Length: 6 } || weights.Any(weight => weight is < 0 or > 10000) || weights.Sum() == 0)
            throw new InvalidDataException($"{path} 必须有 6 个 0–10000 的整数权重，总和须大于 0。");
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Random;

namespace SurpriseRaven.Hooks.Raven;

internal sealed class RavenGameplayOdds
{
    [JsonRequired] public Dictionary<string, int[][]> Outcomes { get; set; } = new(StringComparer.Ordinal);
    [JsonRequired] public int[] InitialLights { get; set; } = [];
    [JsonRequired] public int LightUpPercent { get; set; }
}

internal static class RavenGameplayOddsHooks
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly Lazy<RavenGameplayOdds> Defaults = new(() => new RavenGameplayOdds
    {
        Outcomes = RavenRulesHooks.DefaultOutcomes(),
        InitialLights = [1, 1, 1, 1, 1, 1],
        LightUpPercent = 50
    });
    private static RavenGameplayOdds? _loaded;
    private static RavenGameplayOdds Current => _loaded ?? Defaults.Value;

    internal static int[] Row(RelicRarity tier, int lights) => Current.Outcomes[tier.ToString()][Math.Clamp(lights, 0, 6)];
    internal static int LightUpPercent => Current.LightUpPercent;
    internal static int InitialLights(Rng rng) => Select(Current.InitialLights, rng.NextInt(0, Current.InitialLights.Sum()));

    internal static int Select(int[] weights, int roll)
    {
        int index = 0;
        while (index < weights.Length - 1 && roll >= weights[index]) roll -= weights[index++];
        return index;
    }

    internal static RavenGameplayOdds Parse(string content)
    {
        var odds = JsonSerializer.Deserialize<RavenGameplayOdds>(content, Json) ?? throw new InvalidDataException("Root is null.");
        if (odds.Outcomes is null || odds.Outcomes.Count != Defaults.Value.Outcomes.Count)
            throw new InvalidDataException("outcomes 必须包含 Common、Uncommon、Rare、Ancient 四档。");
        foreach (var (tier, defaults) in Defaults.Value.Outcomes)
        {
            if (!odds.Outcomes.TryGetValue(tier, out var rows) || rows is null || rows.Length != 7)
                throw new InvalidDataException($"outcomes.{tier} 必须有 0–6 灯共七行。");
            for (int lights = 0; lights < rows.Length; lights++)
            {
                CheckWeights(rows[lights], 6, $"outcomes.{tier}[{lights}]");
                for (int index = 0; index < 6; index++)
                    if (rows[lights][index] != 0 && defaults.All(row => row[index] == 0))
                        throw new InvalidDataException($"outcomes.{tier}[{lights}][{index}] 此档位不支持 {(RavenOutcome)index}。");
            }
        }
        CheckWeights(odds.InitialLights, 6, "initialLights");
        if (odds.LightUpPercent is < 0 or > 100)
            throw new InvalidDataException("lightUpPercent 必须是 0–100 的整数。");
        return odds;
    }

    private static void CheckWeights(int[]? weights, int count, string path)
    {
        if (weights is null || weights.Length != count || weights.Any(weight => weight is < 0 or > 10000) || weights.Sum() == 0)
            throw new InvalidDataException($"{path} 必须有 {count} 个 0–10000 的整数权重，总和须大于 0。");
    }

    internal static string Template()
    {
        var content = new StringBuilder("""
            // 渡鸦路灯概率。改好后重启游戏。权重按相对比例抽取。
            // 档位 Common 普通、Uncommon 罕见及商店、Rare 稀有、Ancient 先古。
            // 每档有七行，从上到下是 0 至 6 灯。
            // 每行依次是 [吞噬, 升档, 平换, 降档, 三选一, 原件返还]。
            // 初始灯数按 initialLights 的六个权重抽取。lightUpPercent 是加灯百分比。
            """).AppendLine().AppendLine("{").AppendLine("  \"outcomes\": {");
        var tables = Defaults.Value.Outcomes.ToArray();
        for (int tier = 0; tier < tables.Length; tier++)
        {
            var (name, rows) = tables[tier];
            content.AppendLine($"    \"{name}\": [");
            for (int lights = 0; lights < rows.Length; lights++)
                content.AppendLine($"      [{string.Join(", ", rows[lights])}]{(lights + 1 == rows.Length ? "" : ",")}");
            content.AppendLine(tier + 1 == tables.Length ? "    ]" : "    ],");
        }
        return content.AppendLine("  },")
            .AppendLine($"  \"initialLights\": [{string.Join(", ", Defaults.Value.InitialLights)}],")
            .AppendLine($"  \"lightUpPercent\": {Defaults.Value.LightUpPercent}")
            .AppendLine("}").ToString();
    }

    internal static void Initialize()
    {
        string directory = Path.Combine(OS.GetUserDataDir(), "SurpriseRaven");
        string path = Path.Combine(directory, "gameplay-odds.json");
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(directory);
                using var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, System.IO.FileAccess.Write));
                writer.Write(Template());
            }
            _loaded = Parse(File.ReadAllText(path));
            Logger.Log($"[Raven] Gameplay odds loaded: {path}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            _loaded = Defaults.Value;
            Logger.Log($"[Raven] Gameplay odds defaults used: {path}: {error.Message}");
        }
    }
}

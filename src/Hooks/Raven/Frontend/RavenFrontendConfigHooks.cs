using System.Text.Json;

namespace SurpriseRaven.Hooks.Raven;

public sealed class RavenTimings
{
    public double FadeIn { get; set; } = 0.35;
    public double IgniteStagger { get; set; } = 0.12;
    public double OfferFly { get; set; } = 0.45;
    public double Crouch { get; set; } = 0.25;
    public double FlyAway { get; set; } = 2.4;
    public double GrabFraction { get; set; } = 0.42;
    public double Away { get; set; } = 0.8;
    public double ReturnFly { get; set; } = 2.2;
    public double DropAt { get; set; } = 1.0;
    public double DropFall { get; set; } = 0.45;
    public double SwallowExtinguishAt { get; set; } = 1.7;
    public double SwallowVoiceAt { get; set; } = 2.0;
    public double SwallowEnd { get; set; } = 4.2;
}

public sealed class RavenAudioEvent
{
    public List<string> Clips { get; set; } = [];
    public float Volume { get; set; } = 1f;
    public double Chance { get; set; } = 1.0;
}

public sealed class RavenFrontendConfig
{
    public static readonly string[] RequiredEvents =
    [
        "entry", "result.upgrade", "result.upgrade_sfx", "result.bonus", "reward.swap", "result.swap",
        "result.downgrade", "result.swallow", "candle.light", "candle.out"
    ];

    public string Format { get; set; } = "raven-frontend-v1";
    public float Volume { get; set; } = 0.6f;
    public string ReducedMotion { get; set; } = "auto";
    public RavenTimings Timings { get; set; } = new();
    public Dictionary<string, RavenAudioEvent> Events { get; set; } = [];

    public static RavenFrontendConfig Parse(string json) =>
        JsonSerializer.Deserialize<RavenFrontendConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("Empty raven frontend config.");

    public List<string> Validate(Func<string, bool> clipExists)
    {
        var t = Timings;
        List<string> errors = [];
        if (Format != "raven-frontend-v1") errors.Add($"format {Format}");
        if (Volume is <= 0 or > 1) errors.Add("volume");
        if (ReducedMotion is not ("auto" or "on" or "off")) errors.Add($"reducedMotion {ReducedMotion}");
        foreach (var (name, value) in new[]
        {
            ("fadeIn", t.FadeIn), ("offerFly", t.OfferFly), ("crouch", t.Crouch), ("flyAway", t.FlyAway),
            ("away", t.Away), ("returnFly", t.ReturnFly), ("dropAt", t.DropAt), ("dropFall", t.DropFall),
            ("swallowEnd", t.SwallowEnd), ("igniteStagger", t.IgniteStagger)
        })
            if (value <= 0 || value > 60) errors.Add($"timing {name}");
        if (t.GrabFraction is <= 0 or >= 1) errors.Add("timing grabFraction");
        if (t.DropAt >= t.ReturnFly) errors.Add("dropAt must precede the end of returnFly");
        if (!(t.SwallowExtinguishAt > 0 && t.SwallowExtinguishAt < t.SwallowVoiceAt && t.SwallowVoiceAt < t.SwallowEnd))
            errors.Add("swallow cue order");
        foreach (var name in RequiredEvents)
            if (!Events.TryGetValue(name, out var evt) || evt.Clips.Count == 0) errors.Add($"event {name}");
        foreach (var (name, evt) in Events)
        {
            if (evt.Volume is <= 0 or > 1) errors.Add($"event {name} volume");
            if (evt.Chance is < 0 or > 1) errors.Add($"event {name} chance");
            foreach (var clip in evt.Clips)
                if (!clipExists(clip)) errors.Add($"event {name} clip {clip}");
        }
        return errors;
    }
}

internal static class RavenFrontendConfigHooks
{
    private static RavenFrontendConfig? _current;

    // Packed config when available; built-in timings otherwise (no audio events).
    internal static RavenFrontendConfig Current
    {
        get
        {
            if (_current is not null) return _current;
            try
            {
                var json = RavenAssetsHooks.ReadText("frontend.json");
                _current = json is null ? new RavenFrontendConfig() : RavenFrontendConfig.Parse(json);
                var errors = _current.Validate(id => RavenAssetsHooks.Find(id) is { Kind: "wav" or "mp3" });
                if (errors.Count > 0) Logger.Log($"[Raven] Frontend config issues: {string.Join(", ", errors)}");
            }
            catch (Exception error)
            {
                Logger.Log($"[Raven] Frontend config unreadable, using defaults: {error.Message}");
                _current = new RavenFrontendConfig();
            }
            return _current;
        }
    }
}

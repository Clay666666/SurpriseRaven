using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SurpriseRaven.Hooks.Raven;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: RavenAudioPreview <0.4.4 staged raven directory> <candidate staged raven directory> <output.html>");
    return 2;
}

var audio = new Dictionary<string, string>();
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var baseline = ReadVersion(args[0], false);
var candidate = ReadVersion(args[1], true);
var data = JsonSerializer.Serialize(new { baseline, candidate, audio });
var template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "player.html"));
var js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "player.js"));
var output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, template.Replace("<!--PLAYER_DATA-->", data).Replace("/*PLAYER_CODE*/", js), new UTF8Encoding(false));
Console.WriteLine($"Verified 10 events and 22 audio clips per version ({audio.Count} distinct files). Wrote {output}");
return 0;

object ReadVersion(string directory, bool isCandidate)
{
    var config = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "frontend.json")))!;
    var entries = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")))!["assets"]!.AsArray();
    var events = config["events"]!.AsObject();
    var clips = new Dictionary<string, string>();
    foreach (var entry in entries.Where(e => (string?)e!["kind"] is "mp3" or "wav"))
    {
        string id = (string)entry!["id"]!;
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, (string)entry["path"]!));
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (hash != (string?)entry["sha256"] || bytes.Length != (int)entry["size"]!)
            throw new InvalidDataException($"Staged audio differs from its manifest: {id}");
        clips.Add(id, hash);
        if (!audio.ContainsKey(hash)) audio.Add(hash, Convert.ToBase64String(bytes));
    }
    var referenced = events.SelectMany(e => e.Value!["clips"]!.AsArray()).Select(c => (string)c!).ToHashSet();
    if (events.Count != 10 || clips.Count != 22 || !referenced.SetEquals(clips.Keys))
        throw new InvalidDataException("Every one of the 22 staged clips must belong to the 10 sound events.");
    var t = config["timings"]!.Deserialize<RavenTimings>(options)!;
    double expectedIgnition = isCandidate ? 0.84 : 0.8;
    double expectedUpgrade = isCandidate ? 0.90 : 1;
    if ((double?)events["candle.light"]!["volume"] != expectedIgnition ||
        ((double?)events["result.upgrade_sfx"]!["volume"] ?? 1) != expectedUpgrade || t.SwapSoundLead <= 0 ||
        (double?)events["candle.light"]!["gainDb"] != 6)
        throw new InvalidDataException("The staged sound mix does not match the selected release.");

    var timelines = new Dictionary<string, Dictionary<string, double>>();
    foreach (var (name, swallow, light, swap) in new[] {
        ("return", false, false, false), ("returnLight", false, true, false),
        ("swap", false, false, true), ("swapLight", false, true, true), ("swallow", true, false, false) })
    {
        timelines[name] = RavenTimeline.Schedule(t, swallow, light, swap).ToDictionary(c => c.Cue.ToString(), c => c.At);
        if (!isCandidate && light)
            timelines[name]["Ignite"] = timelines[name]["Gone"] + t.IgniteStagger; // Actual 0.4.4 timing.
    }
    return new { events, volume = (double)config["volume"]!, swapSoundLead = t.SwapSoundLead, clips, timelines };
}

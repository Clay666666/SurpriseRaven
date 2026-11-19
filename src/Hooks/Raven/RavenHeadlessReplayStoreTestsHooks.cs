using System.Text;
using System.Text.Json.Nodes;
using Godot;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    internal static void TestReplayJournalStore()
    {
        var directory = ProjectSettings.GlobalizePath("user://raven-replay-fixture/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var profile = Path.Combine(directory, "a", "current_run.save");
        var other = Path.Combine(directory, "b", "current_run.save");
        var run = JsonNode.Parse("""{"start_time":1,"rng":{"seed":"X"},"visited_map_coords":[],"players":[],"save_time":1,"_ritsulib":{"draw":4}}""")!;
        string Key(string path)
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(run.ToJsonString()));
            return RavenReplayJournalHooks.CreateKey(path, input);
        }
        var anchor = Key(profile);
        run["save_time"] = 1234;
        run["num_reloads"] = 3;
        Check(anchor == Key(profile) && anchor != Key(other), "Reload metadata must be ignored within the same profile only.");
        RavenReplayJournalHooks.SaveAt(directory, anchor, "original preview");
        Check(RavenReplayJournalHooks.ReadAt(directory, anchor) == "original preview", "The durable checkpoint must round-trip.");
        run["_ritsulib"]!["draw"] = 5;
        var advanced = Key(profile);
        Check(anchor != advanced && RavenReplayJournalHooks.ReadAt(directory, advanced) is null, "Changed Mod RNG must disqualify the old checkpoint.");
        RavenReplayJournalHooks.SaveAt(directory, advanced, "second preview");
        RavenReplayJournalHooks.DiscardAt(directory, anchor);
        Check(RavenReplayJournalHooks.ReadAt(directory, advanced) == "second preview", "Old callbacks cannot discard the newer checkpoint.");
        var file = Directory.GetFiles(directory, "*.json").Single();
        var envelope = JsonNode.Parse(File.ReadAllText(file))!;
        envelope["Payload"] = "bad data";
        File.WriteAllText(file, envelope.ToJsonString());
        bool refused = false;
        try { RavenReplayJournalHooks.ReadAt(directory, advanced); }
        catch (InvalidDataException) { refused = true; }
        Check(refused, "A damaged replay payload must be rejected.");
        RavenReplayJournalHooks.SaveAt(directory, advanced, "good");
        RavenReplayJournalHooks.DiscardAt(directory, advanced);
        Check(RavenReplayJournalHooks.ReadAt(directory, advanced) is null, "A closed opportunity must be discarded.");
    }
}

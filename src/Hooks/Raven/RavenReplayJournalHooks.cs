using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Saves;

namespace SurpriseRaven.Hooks.Raven;

// Store the replay beside the Mod's local data. Native and Steam Cloud run saves remain owned by the game.
internal static class RavenReplayJournalHooks
{
    private sealed record Envelope(int Version, string Anchor, string Payload, string Checksum);
    private static string JournalRoot => ProjectSettings.GlobalizePath("user://SurpriseRaven/replay");
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    internal static string HostKey()
    {
        var path = Path.GetFullPath(ProjectSettings.GlobalizePath(
            SaveManager.Instance.GetProfileScopedPath(Path.Combine("saves", "current_run.save"))));
        using var file = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return CreateKey(path, file);
    }

    internal static string CreateKey(string profilePath, Stream source)
    {
        var root = JsonNode.Parse(source) as JsonObject ?? throw new InvalidDataException("Invalid native save.");
        foreach (var field in new[] { "start_time", "rng", "visited_map_coords", "players" })
            if (!root.ContainsKey(field)) throw new InvalidDataException($"Missing native run field {field}.");
        // The room descriptor is part of the anchor. Two native checkpoints on one floor can differ.
        foreach (var counter in new[] { "num_reloads", "run_time", "save_time" }) root.Remove(counter);
        var normalizedPath = Path.GetFullPath(profilePath);
        if (OperatingSystem.IsWindows()) normalizedPath = normalizedPath.ToUpperInvariant();
        return Digest(Encoding.UTF8.GetBytes(normalizedPath)) + "-" + RavenReplayProjectionHooks.Digest(root);
    }

    internal static void Save(string key, string json)
    {
        if (HostKey() != key) throw new IOException("The native checkpoint changed during Raven recording.");
        SaveAt(JournalRoot, key, json);
    }

    internal static string? Read(string key) => ReadAt(JournalRoot, key);
    internal static void Discard(string key) => DiscardAt(JournalRoot, key);

    private static string FileFor(string directory, string key)
    {
        if (key.Length != 129 || key[64] != '-' || !key[..64].All(Uri.IsHexDigit) || !key[65..].All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid Raven replay key.");
        return Path.Combine(directory, key[..64] + ".json");
    }

    internal static void SaveAt(string directory, string key, string json)
    {
        var target = FileFor(directory, key);
        Directory.CreateDirectory(directory);
        using var lease = File.Open(target + ".lock", FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, FileShare.None);
        var tmp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Envelope(1, key, json, Digest(Encoding.UTF8.GetBytes(json))));
                stream.Flush(true);
            }
            File.Move(tmp, target, true);
        }
        finally { File.Delete(tmp); }
    }

    internal static string? ReadAt(string directory, string key)
    {
        var path = FileFor(directory, key);
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read | FileShare.Delete);
        var item = JsonSerializer.Deserialize<Envelope>(stream);
        if (item is not { Version: 1 } || item.Anchor != key) return null;
        if (item.Payload is null || item.Checksum != Digest(Encoding.UTF8.GetBytes(item.Payload)))
            throw new InvalidDataException("Damaged Raven replay record.");
        return item.Payload;
    }

    internal static void DiscardAt(string directory, string key)
    {
        var path = FileFor(directory, key);
        if (!File.Exists(path)) return;
        using var lease = File.Open(path + ".lock", FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, FileShare.None);
        if (ReadAt(directory, key) is not null) File.Delete(path);
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace SurpriseRaven.Hooks.Raven;

public sealed class RavenAssetEntry
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int[]? Grid { get; set; }
    public double DurationSeconds { get; set; }
}

// Raw media packed into the Mod PCK (res://SurpriseRaven/raven/); decoded at runtime.
internal static class RavenAssetsHooks
{
    internal const string Root = "res://SurpriseRaven/raven/";
    private static List<RavenAssetEntry>? _manifest;
    private static readonly Dictionary<string, Texture2D?> Textures = [];
    private static readonly Dictionary<string, AudioStream?> Sounds = [];

    internal static IReadOnlyList<RavenAssetEntry> Manifest
    {
        get
        {
            if (_manifest is not null) return _manifest;
            _manifest = [];
            try
            {
                var json = ReadText("manifest.json");
                if (json is null) Logger.Log("[Raven] Asset manifest missing; using procedural visuals without audio.");
                else
                {
                    using var document = JsonDocument.Parse(json);
                    _manifest = JsonSerializer.Deserialize<List<RavenAssetEntry>>(
                        document.RootElement.GetProperty("assets").GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                }
            }
            catch (Exception error) { Logger.Log($"[Raven] Asset manifest unreadable: {error.Message}"); }
            return _manifest;
        }
    }

    internal static bool Available => Manifest.Count > 0;
    internal static RavenAssetEntry? Find(string id) => Manifest.FirstOrDefault(entry => entry.Id == id);

    internal static string? ReadText(string relative)
    {
        var path = Root + relative;
        return Godot.FileAccess.FileExists(path) ? Godot.FileAccess.GetFileAsString(path) : null;
    }

    internal static byte[]? ReadBytes(RavenAssetEntry entry)
    {
        var path = Root + entry.Path;
        if (!Godot.FileAccess.FileExists(path)) return null;
        var bytes = Godot.FileAccess.GetFileAsBytes(path);
        return bytes.Length == entry.Size ? bytes : null;
    }

    internal static bool Verify(RavenAssetEntry entry) => ReadBytes(entry) is { } bytes
        && Convert.ToHexString(SHA256.HashData(bytes)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase);

    internal static Texture2D? Texture(string id)
    {
        if (Textures.TryGetValue(id, out var cached)) return cached;
        Texture2D? texture = null;
        try
        {
            if (Find(id) is { } entry && ReadBytes(entry) is { } bytes)
            {
                var image = new Image();
                var error = entry.Kind == "png" ? image.LoadPngFromBuffer(bytes) : image.LoadWebpFromBuffer(bytes);
                if (error == Error.Ok)
                {
                    image.GenerateMipmaps();
                    texture = ImageTexture.CreateFromImage(image);
                }
                else Logger.Log($"[Raven] Texture {id} failed to decode: {error}");
            }
        }
        catch (Exception error) { Logger.Log($"[Raven] Texture {id} failed: {error.Message}"); }
        Textures[id] = texture;
        return texture;
    }

    internal static AudioStream? Audio(string id)
    {
        if (Sounds.TryGetValue(id, out var cached)) return cached;
        AudioStream? stream = null;
        try
        {
            if (Find(id) is { Kind: "wav" or "mp3" } entry && ReadBytes(entry) is { } bytes)
            {
                stream = entry.Kind == "wav"
                    ? AudioStreamWav.LoadFromBuffer(bytes, new Godot.Collections.Dictionary())
                    : AudioStreamMP3.LoadFromBuffer(bytes);
                if (stream is null) Logger.Log($"[Raven] Audio {id} failed to decode.");
            }
        }
        catch (Exception error) { Logger.Log($"[Raven] Audio {id} failed: {error.Message}"); }
        Sounds[id] = stream;
        return stream;
    }
}

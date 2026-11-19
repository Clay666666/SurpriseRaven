using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using MegaCrit.Sts2.Core.Saves;
using STS2RitsuLib.RunData;

namespace SurpriseRaven.Hooks.Raven;

// Each confirmed operation is a compare-and-swap on the native run document.
// Replaying it never calls Obtain, awards a card or writes a second unlock.
internal sealed record RavenReplayChange(string Field, bool WasPresent, string Expected,
    bool IsPresent, JsonNode? Value);

internal sealed record RavenReplayStep(string Operation, List<RavenReplayChange> Changes);

internal sealed record RavenReplayRecord(int Format, string GameBuild, string Room, int RoomId, int NextRoomId,
    string Candidate, int CandidateIndex, List<RavenReplayStep> Steps, string FinalDigest);

internal static class RavenReplayProjectionHooks
{
    private const string Registry = "STS2RitsuLib.RunData.RunSavedDataRegistry";
    private static readonly Lazy<Type> Ritsu = new(() => typeof(RunSavedDataStore).Assembly.GetType(Registry, true)!);

    internal static JsonObject Encode(SerializableRun native)
    {
        var plain = Encoding.UTF8.GetBytes(SaveManager.ToJson(native));
        var inject = Ritsu.Value.GetMethod("InjectIntoUtf8Json", BindingFlags.Public | BindingFlags.Static,
            [typeof(byte[]), typeof(SerializableRun)]) ?? throw new MissingMethodException(Registry, "InjectIntoUtf8Json");
        var utf8 = (byte[])inject.Invoke(null, [plain, native])!;
        return JsonNode.Parse(utf8)!.AsObject();
    }

    internal static SerializableRun Decode(JsonObject projected)
    {
        string json = projected.ToJsonString();
        var read = SaveManager.FromJson<SerializableRun>(json);
        if (!read.Success || read.SaveData is null) throw new InvalidDataException("Cannot decode projected run.");
        var attach = Ritsu.Value.GetMethod("AttachDocumentFromJson", BindingFlags.Public | BindingFlags.Static,
            [typeof(SerializableRun), typeof(string)]) ?? throw new MissingMethodException(Registry, "AttachDocumentFromJson");
        attach.Invoke(null, [read.SaveData, json]);
        return read.SaveData;
    }

    // Reload counters and clocks remain the host's; room construction belongs to the room adapter.
    internal static bool Volatile(string key) => key is "num_reloads" or "save_time" or "run_time" or "pre_finished_room";
    internal static string Digest(JsonNode? node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Canonical(writer, node);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void Canonical(Utf8JsonWriter writer, JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            writer.WriteStartObject();
            foreach (var (key, val) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                Canonical(writer, val);
            }
            writer.WriteEndObject();
        }
        else if (node is JsonArray array)
        {
            writer.WriteStartArray();
            foreach (var item in array) Canonical(writer, item);
            writer.WriteEndArray();
        }
        else if (node is null) writer.WriteNullValue();
        else node.WriteTo(writer);
    }

    internal static JsonObject Stable(JsonObject document) => new(document.Where(p => !Volatile(p.Key))
        .Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value?.DeepClone())));

    internal static RavenReplayStep Difference(string operation, JsonObject before, JsonObject after)
    {
        List<RavenReplayChange> changes = [];
        foreach (var field in before.Select(p => p.Key).Union(after.Select(p => p.Key), StringComparer.Ordinal))
        {
            if (Volatile(field)) continue;
            bool existed = before.TryGetPropertyValue(field, out var oldValue);
            bool exists = after.TryGetPropertyValue(field, out var newValue);
            if (existed == exists && JsonNode.DeepEquals(oldValue, newValue)) continue;
            changes.Add(new(field, existed, Digest(oldValue), exists, newValue?.DeepClone()));
        }
        return new(operation, changes);
    }

    internal static JsonObject Apply(JsonObject host, IReadOnlyList<RavenReplayStep> steps, string finalDigest)
    {
        var result = host.DeepClone().AsObject();
        foreach (var step in steps)
            foreach (var change in step.Changes)
            {
                if (Volatile(change.Field)
                    || result.TryGetPropertyValue(change.Field, out var oldValue) != change.WasPresent
                    || Digest(oldValue) != change.Expected)
                    throw new InvalidDataException($"Divergence at {step.Operation}/{change.Field}.");
                if (change.IsPresent) result[change.Field] = change.Value?.DeepClone();
                else result.Remove(change.Field);
            }
        if (Digest(Stable(result)) != finalDigest) throw new InvalidDataException("Incomplete lamp replay.");
        return result;
    }
}

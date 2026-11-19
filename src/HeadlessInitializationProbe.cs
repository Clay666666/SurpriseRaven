using Godot;
using STS2RitsuLib.Interop;
using System.Text.Json;

namespace SurpriseRaven;

// Activated only by the isolated headless test runner, never by normal gameplay.
internal static class HeadlessInitializationProbe
{
    internal static string? ValidateEnvironment()
    {
        if (System.Environment.GetEnvironmentVariable("STS2_SURPRISE_RAVEN_HEADLESS_TEST") != "1")
            return null;

        var root = System.Environment.GetEnvironmentVariable("STS2_SURPRISE_RAVEN_TEST_ROOT");
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("Headless test requires an absolute isolated root.");
        if (DisplayServer.GetName() != "headless")
            throw new InvalidOperationException("Headless test refuses a graphical display server.");

        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var path in new[] { OS.GetUserDataDir(), OS.GetCacheDir() })
        {
            if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Game data path escapes the isolated root: {path}");
        }
        return Path.GetFullPath(root);
    }

    internal static void Complete(string? testRoot)
    {
        if (testRoot is null) return;
        if (Engine.GetMainLoop() is not SceneTree tree)
            throw new InvalidOperationException("No SceneTree is available for test shutdown.");
        Callable.From(() => RunChecksAndQuit(testRoot, tree)).CallDeferred();
    }

    private static async void RunChecksAndQuit(string testRoot, SceneTree tree)
    {
        try
        {
            var checks = Hooks.Raven.RavenHeadlessTestsHooks.Run();
            var nodeChecks = await Hooks.Raven.RavenOverlayHooks.RunNodeChecksAsync();
            File.WriteAllText(Path.Combine(testRoot, "raven-headless.json"),
                JsonSerializer.Serialize(new { status = "passed", checks, node_checks = nodeChecks },
                    new JsonSerializerOptions { WriteIndented = true }));
            WriteSuccess(testRoot);
            GD.Print("RAVEN_HEADLESS_TESTS_OK");
            tree.Quit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(testRoot, "raven-headless.json"),
                JsonSerializer.Serialize(new { status = "failed", error = error.ToString() },
                    new JsonSerializerOptions { WriteIndented = true }));
            GD.PushError("RAVEN_HEADLESS_TESTS_FAILED: " + error);
            tree.Quit(1);
        }
    }

    private static void WriteSuccess(string testRoot)
    {
        var result = new
        {
            status = "passed",
            mod_id = ModEntry.ModId,
            release_version = ModEntry.ReleaseVersion,
            mod_version = ModEntry.BuildAssembly.GetName().Version?.ToString(),
            source_commit = ModEntry.BuildCommit,
            display_server = DisplayServer.GetName(),
            data_directory = OS.GetUserDataDir(),
            cache_directory = OS.GetCacheDir(),
            ritsulib_version = typeof(ModTypeDiscoveryHub).Assembly.GetName().Version?.ToString()
        };
        File.WriteAllText(Path.Combine(testRoot, "headless-probe.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("RITSULIB_HEADLESS_PROBE_OK");
    }
}

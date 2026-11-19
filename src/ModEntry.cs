using HarmonyLib;
using System.Reflection;
using MegaCrit.Sts2.Core.Modding;
using STS2RitsuLib;
using STS2RitsuLib.Interop;

namespace SurpriseRaven;

[ModInitializer("Initialize")]
public static class ModEntry
{
    public static readonly string ModId = "SurpriseRaven";
    public static readonly string ModName = "惊喜渡鸦 / SurpriseRaven";
    internal static readonly Assembly BuildAssembly = typeof(ModEntry).Assembly;
    internal static string ReleaseVersion => BuildAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion;
    internal static string BuildCommit =>
        BuildAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "RavenSourceCommit")?.Value ?? "unrecorded";

    public static void Initialize()
    {
        var testRoot = HeadlessInitializationProbe.ValidateEnvironment();
        ModTypeDiscoveryHub.RegisterModAssembly(ModId, typeof(ModEntry).Assembly);
        RitsuLibFramework.CreateLogger(ModId).Info("RitsuLib assembly discovery registered.");
        Hooks.Raven.RavenServiceHooks.Initialize();
        var harmony = new Harmony(ModId);
        harmony.PatchAll();
        Logger.Log($"[{ModName}] Mod loaded! Hooks active. Version={ReleaseVersion}, Assembly={BuildAssembly.GetName().Version}, commit={BuildCommit}, DLL={BuildAssembly.Location}");
        HeadlessInitializationProbe.Complete(testRoot);
    }
}

public static class Logger
{
    private static readonly string LogPath;

    static Logger()
    {
        LogPath = Path.Combine(
            Godot.OS.GetUserDataDir(), "logs", "mod_log.txt"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
    }

    public static void Log(string message)
    {
        File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}

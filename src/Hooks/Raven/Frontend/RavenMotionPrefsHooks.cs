using System.Runtime.InteropServices;

namespace SurpriseRaven.Hooks.Raven;

// Reduced motion follows the Windows "Show animations" setting (SPI_GETCLIENTAREAANIMATION), the
// signal browsers map to prefers-reduced-motion. raven-frontend.json "reducedMotion" can force it.
internal static class RavenMotionPrefsHooks
{
    private const uint GetClientAreaAnimation = 0x1042;
    private static bool _logged;

    internal static bool? Override { get; set; }

    internal static bool Reduced => Override ?? RavenFrontendConfigHooks.Current.ReducedMotion switch
    {
        "on" => true,
        "off" => false,
        _ => SystemReduced()
    };

    private static bool SystemReduced()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try { return SystemParametersInfo(GetClientAreaAnimation, 0, out int enabled, 0) && enabled == 0; }
        catch (Exception error)
        {
            if (!_logged) Logger.Log($"[Raven] Reduced-motion preference unavailable: {error.Message}");
            _logged = true;
            return false;
        }
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);
}

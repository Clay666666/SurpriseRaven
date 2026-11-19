using System.Collections;
using System.Reflection;
using Godot;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    // Exercise the framework's actual order preview and row renderer in the isolated mirror.
    private static void TestRitsuLoadOrderName()
    {
        var coordinator = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("STS2RitsuLib.Settings.ContentModLoadOrderCoordinator"))
            .First(type => type is not null)!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        var preview = coordinator.GetMethod("BuildPreview", flags)!.Invoke(null, null)!;
        var bootstrap = coordinator.Assembly.GetType("STS2RitsuLib.Settings.RitsuLibModSettingsBootstrap", true)!;
        var createRow = bootstrap.GetMethod("CreateOrderRow", flags)!;
        const string expectedText = "惊喜渡鸦 / SurpriseRaven (SurpriseRaven)";
        foreach (var column in new[] { "Current", "Target" })
        {
            var items = ((IEnumerable)preview.GetType().GetProperty(column)!.GetValue(preview)!).Cast<object>().ToArray();
            string Id(object item) => (string)item.GetType().GetProperty("Id")!.GetValue(item)!;
            Check(items.All(item => Id(item) != RavenStorageHooks.PersistenceId), $"Retired Mod in {column}.");
            var entry = items.Single(item => Id(item) == ModEntry.ModId);
            var row = (Control)createRow.Invoke(null, [entry, string.Empty])!;
            try
            {
                bool matches = row.FindChildren("*", "", true, false).Any(child => child switch
                {
                    Label label => label.Text == expectedText,
                    RichTextLabel rich => rich.GetParsedText() == expectedText,
                    _ => false
                });
                Check(matches, $"Unexpected RitsuLib {column} row. Expected {expectedText}.");
                Logger.Log($"[Raven] RitsuLib {column} confirmed: {expectedText}");
            }
            finally { row.Free(); }
        }
    }
}

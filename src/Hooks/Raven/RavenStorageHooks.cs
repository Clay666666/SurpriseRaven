using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Random;
using STS2RitsuLib;
using STS2RitsuLib.RunData;
using STS2RitsuLib.RunRngs;

namespace SurpriseRaven.Hooks.Raven;

// RitsuLib owns save attachment and RNG restoration; never force a room save here.
internal static class RavenStorageHooks
{
    // This key is part of the 0.4.2 save format, including the seeds of the per-player streams.
    internal const string PersistenceId = "Sts2ModScaffold";
    private static PlayerRunSavedData<RavenState>? _slot;

    internal static void Initialize()
    {
        if (_slot is not null) return;
        _slot = RitsuLibFramework.GetRunSavedDataStore(PersistenceId)
            .RegisterPerPlayer("raven_lamp", () => new RavenState(), new RunSavedDataOptions
            {
                SchemaVersion = 1,
                WritePolicy = RunSavedDataWritePolicy.WhenSet,
                SyncLobbyOnChange = false
            });
        Logger.Log("[Raven] Player run data registered (schema 1).");
    }

    internal static RavenState Get(Player player)
    {
        Initialize();
        return _slot!.Get(player);
    }

    // Get returns the attached object; Modify marks its current contents as set.
    internal static void Touch(Player player)
    {
        Initialize();
        _slot!.Modify(player, static _ => { });
    }

    internal static Rng Rng(Player player, string stream) =>
        ModRunRngRegistry.Get(player, PersistenceId, "raven." + stream);
}

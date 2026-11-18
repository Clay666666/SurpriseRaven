using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using STS2RitsuLib.RunData;

namespace SurpriseRaven.Hooks.Raven;

public sealed class RavenNativeAncientState
{
    public bool AllocationsCaptured { get; set; }
    public Dictionary<string, List<string>> SharedByAct { get; set; } = [];
    public Dictionary<string, RavenNativeAncientVisit> Visits { get; set; } = [];
}

public sealed class RavenNativeAncientVisit
{
    public string AncientId { get; set; } = "";
    public bool Exhausted { get; set; }
}

internal static class RavenNativeAncientStorageHooks
{
    private sealed class SharedAllocation(List<string> ids) { internal List<string> Ids { get; } = ids; }
    private static readonly ConditionalWeakTable<ActModel, SharedAllocation> Allocations = new();
    private static PlayerRunSavedData<RavenNativeAncientState>? _slot;

    private static PlayerRunSavedData<RavenNativeAncientState> Slot => _slot ??= RitsuLibFramework.GetRunSavedDataStore(RavenStorageHooks.PersistenceId)
        .RegisterPerPlayer("raven_native_ancients", () => new RavenNativeAncientState(), new RunSavedDataOptions
        { SchemaVersion = 1, WritePolicy = RunSavedDataWritePolicy.WhenSet, SyncLobbyOnChange = false });

    internal static void Initialize() => _ = Slot;
    internal static RavenNativeAncientState Get(Player player) => Slot.Get(player);
    internal static void Touch(Player player) => Slot.Modify(player, static _ => { });
    internal static string ActKey(Player player, ActModel act) =>
        $"{player.RunState.Acts.ToList().FindIndex(a => ReferenceEquals(a, act))}:{act.Id}";

    internal static void Capture(ActModel act, IEnumerable<AncientEventModel> shared)
    {
        Allocations.Remove(act);
        Allocations.Add(act, new SharedAllocation(shared.Select(a => a.Id.ToString()).ToList()));
    }

    internal static void SaveAllocations(Player player, RavenNativeAncientState data)
    {
        foreach (var act in player.RunState.Acts)
            data.SharedByAct[ActKey(player, act)] = Allocations.TryGetValue(act, out var saved) ? saved.Ids.ToList() : [];
        data.AllocationsCaptured = true;
    }

    internal static List<AncientEventModel> Candidates(Player player, ActModel act, RavenNativeAncientState data)
    {
        var candidates = act.GetUnlockedAncients(player.UnlockState).ToList();
        List<string> shared;
        if (data.AllocationsCaptured) shared = data.SharedByAct.GetValueOrDefault(ActKey(player, act)) ?? [];
        else if (Allocations.TryGetValue(act, out var allocation)) shared = allocation.Ids;
        else shared = [act.Ancient.Id.ToString()]; // Legacy save: only the selected shared NPC is proven assigned here.
        var elsewhere = player.RunState.Acts.Where(a => !ReferenceEquals(a, act)).Select(a => a.Ancient.Id).ToHashSet();
        candidates.AddRange(player.UnlockState.SharedAncients.Where(a => shared.Contains(a.Id.ToString()) && !elsewhere.Contains(a.Id)));
        return candidates.Where(RavenNativeAncientPoolsHooks.Supported).DistinctBy(a => a.Id)
            .OrderBy(a => a.Id.ToString(), StringComparer.Ordinal).ToList();
    }

    [HarmonyPatch(typeof(ActModel), nameof(ActModel.SetSharedAncientSubset))]
    private static class SharedAllocationPatch
    {
        [HarmonyPostfix]
        private static void RavenCaptureSharedAncientsPostfix(ActModel __instance, List<AncientEventModel> sharedAncientSubset) =>
            Capture(__instance, sharedAncientSubset);
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.GenerateRooms))]
    private static class GeneratedRoomsPatch
    {
        [HarmonyPostfix]
        private static void RavenSaveAncientAllocationsPostfix(RunManager __instance)
        {
            var run = __instance.DebugOnlyGetState();
            if (run is null || run.Players.Count != 1) return;
            var player = run.Players[0];
            SaveAllocations(player, Get(player));
            Touch(player);
        }
    }
}

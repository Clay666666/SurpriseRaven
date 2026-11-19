using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace SurpriseRaven.Hooks.Raven;

// Only this overload is patched: Obtain<T> delegates to it in the local game API.
[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain),
    new[] { typeof(RelicModel), typeof(Player), typeof(int) })]
internal static class RavenAcquisitionHooks
{
    internal sealed class AcquisitionScope
    {
        private int _finished;
        private readonly Action _finish;

        internal bool IsDirectNeowAncient { get; }
        internal long Generation { get; } = RavenServiceHooks.Generation;

        internal AcquisitionScope(Action? finish = null, bool isDirectNeowAncient = false)
        {
            _finish = finish ?? RavenServiceHooks.AcquisitionFinished;
            IsDirectNeowAncient = isDirectNeowAncient;
        }

        internal void Complete()
        {
            if (Interlocked.Exchange(ref _finished, 1) == 0)
                _finish();
        }
    }

    [HarmonyPrefix]
    private static bool RavenObtainPrefix(Player player, RelicModel relic, out AcquisitionScope? __state,
        ref Task<RelicModel> __result)
    {
        bool live = RavenServiceHooks.BindLivePlayer(player);
        if (live && !RavenNativeRelicOfferHooks.CanClaimNative(player, relic, includePurchases: false))
        {
            __state = null;
            __result = Task.FromResult(player.Relics.FirstOrDefault(held => held.Id == relic.Id) ?? relic);
            Logger.Log($"[Raven] Native acquisition rejected before insertion and pickup effects: {relic.Id}.");
            return false;
        }
        bool neow = live && RavenAcquisitionSourceHooks.CaptureDirectNeowAncient(player, relic);
        __state = new AcquisitionScope(live ? RavenServiceHooks.BeginObservedAcquisition() : static () => { },
            neow);
        return true;
    }

    [HarmonyPostfix]
    private static void RavenObtainPostfix(Player player, AcquisitionScope? __state,
        ref Task<RelicModel> __result)
    {
        if (__state is null) return;
        __result = ObserveObtain(__result,
            obtained => RavenServiceHooks.OnObtained(player, obtained, __state.IsDirectNeowAncient), __state);
    }

    internal static async Task<RelicModel> ObserveObtain(
        Task<RelicModel> original, Action<RelicModel> notify, AcquisitionScope scope)
    {
        try
        {
            // Keep the game's synchronization context and original failure semantics.
            var obtained = await original;
            try
            {
                if (scope.Generation == RavenServiceHooks.Generation) notify(obtained);
            }
            catch (Exception error)
            {
                // A Mod notification must not turn a successful original award into a failure.
                Logger.Log($"[Raven] Acquisition notification failed: {error}");
            }
            return obtained;
        }
        finally
        {
            scope.Complete();
        }
    }

    [HarmonyFinalizer]
    private static Exception? RavenObtainFinalizer(Exception? __exception,
        AcquisitionScope? __state)
    {
        // Original synchronous exceptions skip Postfix, hence need their own balance.
        if (__exception is not null) __state?.Complete();
        return __exception;
    }
}

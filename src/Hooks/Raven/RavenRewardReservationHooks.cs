using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

[HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.BeginRewardsSet))]
internal static class RavenRewardReservationHooks
{
    private static readonly ConditionalWeakTable<Player, List<Reservation>> Active = new();

    internal sealed class Reservation
    {
        internal RewardsSet Set { get; }
        internal IRunState Run { get; }
        internal AbstractRoom? Room { get; }
        private readonly List<Reservation> _active;

        internal Reservation(RewardsSet set, List<Reservation> active)
        {
            Set = set;
            Run = set.Player.RunState;
            Room = Run.BaseRoom;
            _active = active;
        }

        internal void Release()
        {
            lock (_active) _active.Remove(this);
        }
    }

    internal static Reservation Track(RewardsSet set)
    {
        var active = Active.GetOrCreateValue(set.Player);
        var reservation = new Reservation(set, active);
        lock (active) active.Add(reservation);
        return reservation;
    }

    [HarmonyPrefix]
    private static void RavenBeginRewardsPrefix(RewardsSet set, out Reservation? __state) =>
        __state = RavenServiceHooks.IsSingleplayer ? Track(set) : null;

    [HarmonyPostfix]
    private static void RavenBeginRewardsPostfix(Reservation? __state, ref Task __result)
    {
        if (__state is not null) __result = UntilFinished(__result, __state);
    }

    [HarmonyFinalizer]
    private static void RavenBeginRewardsFinalizer(Reservation? __state, Exception? __exception)
    {
        if (__exception is not null) __state?.Release();
    }

    internal static async Task UntilFinished(Task original, Reservation reservation)
    {
        try { await original.ConfigureAwait(false); }
        finally { reservation.Release(); }
    }

    internal static void IncludePending(Player player, HashSet<ModelId> ids)
    {
        if (!Active.TryGetValue(player, out var active)) return;
        var run = player.RunState;
        var room = run.BaseRoom;
        lock (active)
            foreach (var reservation in active)
                if (ReferenceEquals(reservation.Run, run) && ReferenceEquals(reservation.Room, room))
                    IncludeRewards(reservation.Set.Rewards, ids);
    }

    private static void IncludeRewards(IEnumerable<Reward> rewards, HashSet<ModelId> ids)
    {
        foreach (var reward in rewards)
        {
            if (reward is LinkedRewardSet linked) IncludeRewards(linked.Rewards, ids);
            else if (reward is RelicReward { IsPopulated: true, SuccessfullySelected: false } relic)
                ids.Add((relic.Relic ?? throw new InvalidOperationException("Populated relic reward has no model.")).Id);
        }
    }
}

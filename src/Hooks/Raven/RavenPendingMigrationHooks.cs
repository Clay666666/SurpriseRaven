using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenServiceHooks
{
    // Old saved results were locked before removing their source. Normalize once, without rolling.
    private static async Task NormalizePendingAsync(Player player)
    {
        var state = RavenStorageHooks.Get(player);
        if (state.Pending is not { } pending || !CanStart(player, state, state.Revision)
            || RavenTransactionHooks.IsReady(pending)) return;
        await RunOperationAsync(player, state, async () =>
        {
            if (pending.SettlementVersion is not (0 or 1))
                throw new InvalidOperationException($"Incomplete or unsupported pending protocol {pending.SettlementVersion}; refusing replay.");
            if (pending.SettlementVersion == 1)
            {
                await RavenTransactionHooks.CommitAsync(state, pending,
                    () => throw new InvalidOperationException("Committed v1 source must never be removed again."),
                    () => RavenStorageHooks.Touch(player), legacy: true);
                return;
            }
            var stake = ResolveStake(player, pending);
            if (stake is null && pending.Outcome != RavenOutcome.Bonus)
                throw new InvalidOperationException($"Legacy stake {pending.StakeId} is missing; reward remains blocked.");
            if (stake is not null)
            {
                pending.StakeSnapshot = stake.ToSerializable();
                pending.StakeIndex = player.Relics.ToList().IndexOf(stake);
                pending.StakeTitle = stake.Title.GetFormattedText();
            }
            bool committed = await RavenTransactionHooks.CommitAsync(state, pending,
                () => RelicCmd.Remove(stake!), () => RavenStorageHooks.Touch(player), legacy: true);
            if (committed)
                Logger.Log($"[Raven] Legacy pending normalized without reroll: {pending.Outcome}, consumed={pending.StakeConsumed}.");
        });
    }
}

using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;

namespace SurpriseRaven.Hooks.Raven;

internal static class RavenNativeAncientSpecialHooks
{
    internal static void AddOrobasPools(RavenNativeAncientPoolsHooks plan, AncientEventModel ancient)
    {
        // Native marginal weights: Shrymp 3/9, Eye 3/9, Gem 1/9, SeaGlass 2/9.
        // A single parameterized SeaGlass object is initialized only if that option is selected.
        var ordinary = RavenNativeAncientPoolsHooks.Pool(ancient, "OptionPool1");
        List<EventOption> first = [.. ordinary, .. ordinary, .. ordinary];
        first.Add(RavenNativeAncientPoolsHooks.Option(ancient, "PrismaticGemOption"));
        var seaGlass = RavenNativeAncientPoolsHooks.Pool(ancient, "SeaGlassOptions").FirstOrDefault();
        if (seaGlass is not null) first.AddRange([seaGlass, seaGlass]);
        plan.Rows.AddRange([first, RavenNativeAncientPoolsHooks.Pool(ancient, "OptionPool2"),
            RavenNativeAncientPoolsHooks.Pool(ancient, "OptionPool3")]);
    }

    internal static void AddDarvPools(RavenNativeAncientPoolsHooks plan, AncientEventModel ancient, Player player)
    {
        plan.Darv = true;
        var options = ancient.AllPossibleOptions.ToList();
        plan.Extra = options.Where(o => o.Relic is DustyTome && RavenAncientSetupHooks.TomeCards(player).Any()).ToList();
        var normal = options.Where(o => o.Relic is not DustyTome).ToList();
        normal.RemoveAll(o => o.Relic switch
        {
            Ectoplasm or Sozu => player.RunState.CurrentActIndex != 1,
            PhilosophersStone or VelvetChoker => player.RunState.CurrentActIndex < 1,
            PandorasBox => player.RunState.Modifiers.Any(m => m.ClearsPlayerDeck),
            _ => false
        });
        plan.Rows.Add(normal);
    }

    internal static void InitializeSelected(EventOption option, Player player, Rng rng)
    {
        // TouchOfOrobas and ArchaicTooth were already initialized by the native pool getter.
        switch (option.Relic)
        {
            case DustyTome tome:
                // Preserve native Rewards-stream initialization, exactly once for the chosen option.
                tome.SetupForPlayer(player);
                break;
            case SeaGlass glass:
                var characters = player.UnlockState.Characters.Where(c => c.Id != player.Character.Id).ToList();
                glass.CharacterId = characters.Count == 0 ? player.Character.Id : characters[rng.NextInt(characters.Count)].Id;
                break;
        }
    }
}

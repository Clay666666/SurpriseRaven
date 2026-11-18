using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SurpriseRaven.Hooks.Raven;

// Predicates verified against the local 0.111.0 relic/event implementations.
internal static class RavenAncientEligibilityHooks
{
    internal static bool IsAllowed(Player player, RelicModel relic)
    {
        var cards = player.Deck.Cards;
        var run = player.RunState;
        return relic switch
        {
            TouchOfOrobas touch => CanRefine(player, touch),
            ArchaicTooth => ((ArchaicTooth)ModelDb.Relic<ArchaicTooth>().ToMutable()).SetupForPlayer(player),
            DustyTome => RavenAncientSetupHooks.TomeCards(player).Any(),
            PaelsLegion => !player.HasEventPet(),
            LargeCapsule => HasBasic(player, CardTag.Strike) && HasBasic(player, CardTag.Defend),
            PaelsClaw => cards.Any(ModelDb.Enchantment<Goopy>().CanEnchant),
            TriBoomerang => cards.Count(ModelDb.Enchantment<Instinct>().CanEnchant) >= 3,
            BeautifulBracelet => cards.Count(ModelDb.Enchantment<Swift>().CanEnchant) >= 4,
            PaelsTooth => cards.Count(c => c.IsRemovable && c.IsUpgradable) >= 5,
            NutritiousSoup => cards.Any(c => c.Rarity == CardRarity.Basic
                && c.Tags.Contains(CardTag.Strike) && ModelDb.Enchantment<TezcatarasEmber>().CanEnchant(c)),
            PandorasBox => !run.Modifiers.Any(m => m.ClearsPlayerDeck)
                && cards.Any(c => c.IsBasicStrikeOrDefend && c.IsTransformable),
            GoldenCompass => run.ActFloor == 0,
            Ectoplasm or Sozu => run.CurrentActIndex == 1,
            PhilosophersStone or VelvetChoker => run.CurrentActIndex >= 1,
            Kaleidoscope or ScrollBoxes => relic.IsAllowedAtNeow(player),
            _ => true
        };
    }

    private static bool HasBasic(Player player, CardTag tag) => player.Character.CardPool.AllCards
        .Any(c => c.Rarity == CardRarity.Basic && c.Tags.Contains(tag));

    private static bool CanRefine(Player player, TouchOfOrobas relic)
    {
        // Validate a fresh clone, not a saved offer's previously initialized properties.
        var probe = (TouchOfOrobas)ModelDb.Relic<TouchOfOrobas>().ToMutable();
        if (!probe.SetupForPlayer(player)) return false;
        if (probe.UpgradedRelic == ModelDb.Relic<Circlet>().Id) return false;
        return !player.Relics.Any(r => r.Id == probe.UpgradedRelic);
    }
}

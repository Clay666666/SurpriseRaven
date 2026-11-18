using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;

namespace SurpriseRaven.Hooks.Raven;

internal static class RavenAncientSetupHooks
{
    internal static IEnumerable<CardModel> TomeCards(Player player) => player.Character.CardPool
        .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
        .Where(c => c.Rarity == CardRarity.Ancient && !ArchaicTooth.TranscendenceCards.Contains(c));

    internal static bool Initialize(Player player, RelicModel relic, Rng rng)
    {
        switch (relic)
        {
            case TouchOfOrobas touch:
                return touch.SetupForPlayer(player);
            case ArchaicTooth tooth:
                return tooth.SetupForPlayer(player);
            case DustyTome tome:
                var cards = TomeCards(player).OrderBy(c => c.Id.ToString(), StringComparer.Ordinal).ToList();
                if (cards.Count == 0) return false;
                // DustyTome.SetupForPlayer consumes the normal Rewards RNG; use our stream instead.
                tome.AncientCard = cards[rng.NextInt(cards.Count)].Id;
                break;
            case SeaGlass glass:
                var characters = player.UnlockState.Characters.Where(c => c.Id != player.Character.Id)
                    .OrderBy(c => c.Id.ToString(), StringComparer.Ordinal).ToList();
                glass.CharacterId = characters.Count == 0 ? player.Character.Id
                    : characters[rng.NextInt(characters.Count)].Id;
                break;
        }
        return true;
    }

    internal static bool HasValidSavedParameters(Player player, RelicModel relic) => relic switch
    {
        TouchOfOrobas touch => touch.StarterRelic != null && touch.UpgradedRelic != null
            && player.Relics.Any(r => r.Id == touch.StarterRelic
                && touch.GetUpgradedStarterRelic(r).Id == touch.UpgradedRelic)
            && !player.Relics.Any(r => r.Id == touch.UpgradedRelic),
        ArchaicTooth tooth => tooth.StarterCard != null && tooth.AncientCard != null
            && player.Deck.Cards.Any(c => c.Id == tooth.StarterCard.Id),
        DustyTome tome => TomeCards(player).Any(c => c.Id == tome.AncientCard),
        SeaGlass glass => player.UnlockState.Characters.Any(c => c.Id == glass.CharacterId)
            || player.Character.Id == glass.CharacterId,
        _ => true
    };
}

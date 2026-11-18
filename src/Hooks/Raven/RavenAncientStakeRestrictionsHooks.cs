using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SurpriseRaven.Hooks.Raven;

// These pickup effects leave lasting run changes. Restrict stakes by identity only,
// including restored instances and pickups whose optional rewards were skipped.
internal static class RavenAncientStakeRestrictionsHooks
{
    private static readonly Lazy<HashSet<ModelId>> BlockedIds = new(() =>
    [
        // Permanent card rewards
        ModelDb.Relic<PaelsHorn>().Id,
        ModelDb.Relic<Storybook>().Id,
        ModelDb.Relic<SereTalon>().Id,
        ModelDb.Relic<DistinguishedCape>().Id,
        ModelDb.Relic<GlassEye>().Id,
        ModelDb.Relic<SeaGlass>().Id,
        ModelDb.Relic<JewelryBox>().Id,
        ModelDb.Relic<TanxsWhistle>().Id,
        ModelDb.Relic<DustyTome>().Id,

        // Permanent upgrades and enchantments
        ModelDb.Relic<PaelsClaw>().Id,
        ModelDb.Relic<PaelsGrowth>().Id,
        ModelDb.Relic<NutritiousSoup>().Id,
        ModelDb.Relic<YummyCookie>().Id,
        ModelDb.Relic<ElectricShrymp>().Id,
        ModelDb.Relic<SandCastle>().Id,
        ModelDb.Relic<BeautifulBracelet>().Id,
        ModelDb.Relic<TriBoomerang>().Id,

        // Deck removal and transformation
        ModelDb.Relic<PaelsTooth>().Id,
        ModelDb.Relic<BiiigHug>().Id,
        ModelDb.Relic<PreservedFog>().Id,
        ModelDb.Relic<ArchaicTooth>().Id,
        ModelDb.Relic<Claws>().Id,
        ModelDb.Relic<Astrolabe>().Id,
        ModelDb.Relic<EmptyCage>().Id,
        ModelDb.Relic<PandorasBox>().Id,

        // Granted resources
        ModelDb.Relic<ToyBox>().Id,
        ModelDb.Relic<CallingBell>().Id,
        ModelDb.Relic<AlchemicalCoffer>().Id,
        ModelDb.Relic<LoomingFruit>().Id,
        ModelDb.Relic<SignetRing>().Id,

        // Map or starter relic changes
        ModelDb.Relic<GoldenCompass>().Id,
        ModelDb.Relic<TouchOfOrobas>().Id
    ]);

    internal static bool Contains(RelicModel relic) =>
        relic.Rarity == RelicRarity.Ancient && BlockedIds.Value.Contains(relic.Id);
}

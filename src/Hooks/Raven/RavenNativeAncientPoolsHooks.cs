using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;

namespace SurpriseRaven.Hooks.Raven;

// These are native EventOptions, including their original callbacks, costs and text.
internal sealed class RavenNativeAncientPoolsHooks
{
    internal readonly List<List<EventOption>> Rows = [];
    internal List<EventOption> Extra = [];
    internal bool Shared, Darv;
    internal bool Viable => Darv ? Rows[0].Count >= 3 || (Rows[0].Count >= 2 && Extra.Count > 0)
        : Shared ? Rows[0].Select(o => o.Relic?.Id).Distinct().Count() >= 3 : Rows.All(row => row.Count > 0);

    internal static bool Supported(AncientEventModel ancient) => ancient is Pael or Tezcatara or Vakuu
        or Orobas or Nonupeipe or Tanx or MegaCrit.Sts2.Core.Models.Events.Darv;

    internal static RavenNativeAncientPoolsHooks Build(AncientEventModel ancient, Player player, ISet<ModelId> obtained)
    {
        var plan = new RavenNativeAncientPoolsHooks();
        var cards = player.Deck.Cards;
        switch (ancient)
        {
            case Pael:
                var middle = Pool(ancient, "OptionPool2");
                if (cards.Count(ModelDb.Enchantment<Goopy>().CanEnchant) >= 3) middle.Add(Option(ancient, "PaelsClawOption"));
                if (cards.Count(c => c.IsRemovable) >= 5) middle.Add(Option(ancient, "PaelsToothOption"));
                middle.AddRange(middle.ToArray()); // Preserve the original two-to-one weights.
                middle.Add(Option(ancient, "PaelsGrowthOption"));
                var last = Pool(ancient, "OptionPool3");
                if (!player.HasEventPet()) last.Add(Option(ancient, "PaelsLegionOption"));
                plan.Rows.AddRange([Pool(ancient, "OptionPool1"), middle, last]);
                break;
            case Tezcatara:
                var food = Pool(ancient, "OptionPool1");
                if (cards.Any(c => c.Rarity == CardRarity.Basic && c.Tags.Contains(CardTag.Strike)))
                    food.Add(Option(ancient, "NutritiousSoupOption"));
                plan.Rows.AddRange([food, Pool(ancient, "OptionPool2"), Pool(ancient, "OptionPool3")]);
                break;
            case Vakuu:
                plan.Rows.AddRange([Pool(ancient, "Pool1"), Pool(ancient, "Pool2"), Pool(ancient, "Pool3")]);
                break;
            case Orobas:
                RavenNativeAncientSpecialHooks.AddOrobasPools(plan, ancient);
                break;
            case Nonupeipe:
                var clothing = Pool(ancient, "OptionPool");
                if (cards.Count(ModelDb.Enchantment<Swift>().CanEnchant) >= 4)
                    clothing.Add(Option(ancient, "BeautifulBraceletEventOption"));
                plan.Shared = true;
                plan.Rows.Add(clothing);
                break;
            case Tanx:
                var weapons = Pool(ancient, "BaseOptionPool");
                if (cards.Count(ModelDb.Enchantment<Instinct>().CanEnchant) >= 3) weapons.Add(Option(ancient, "TriBoomerangOption"));
                plan.Shared = true;
                plan.Rows.Add(weapons);
                break;
            case MegaCrit.Sts2.Core.Models.Events.Darv:
                RavenNativeAncientSpecialHooks.AddDarvPools(plan, ancient, player);
                break;
            default: throw new InvalidOperationException($"No native pool adapter for {ancient.Id}.");
        }
        foreach (var row in plan.Rows) row.RemoveAll(o => !Allowed(o, player, obtained));
        plan.Extra.RemoveAll(o => !Allowed(o, player, obtained));
        // Native Setup may turn an already-obtained refinement row into its locked placeholder.
        // Preserve natural locks, but do not let that placeholder conceal historical exhaustion.
        if (ancient is Orobas && plan.Rows[2].All(o => o.Relic is null)
            && (obtained.Contains(ModelDb.Relic<TouchOfOrobas>().Id) || obtained.Contains(ModelDb.Relic<ArchaicTooth>().Id)))
            plan.Rows[2].Clear();
        return plan;
    }

    internal List<EventOption> Draw(AncientEventModel ancient, Player player, Rng rng)
    {
        if (!Viable) return [];
        List<EventOption> result;
        if (Darv)
        {
            bool withTome = Extra.Count > 0 && (Rows[0].Count < 3 || rng.NextInt(2) == 0);
            result = DrawDistinct(Rows[0], withTome ? 2 : 3, rng);
            if (withTome) result.Add(Extra[0]);
        }
        else if (Shared) result = DrawDistinct(Rows[0], 3, rng);
        else result = Rows.Select(row => row[rng.NextInt(row.Count)]).ToList();
        foreach (var option in result) RavenNativeAncientSpecialHooks.InitializeSelected(option, player, rng);
        return result;
    }

    private static List<EventOption> DrawDistinct(List<EventOption> source, int count, Rng rng)
    {
        var pool = source.ToList();
        List<EventOption> result = [];
        while (result.Count < count && pool.Count > 0)
        {
            var next = pool[rng.NextInt(pool.Count)];
            result.Add(next);
            pool.RemoveAll(o => o.Relic?.Id == next.Relic?.Id);
        }
        return result;
    }

    private static bool Allowed(EventOption option, Player player, ISet<ModelId> obtained) => option.Relic is not { } relic
        || (!obtained.Contains(relic.Id) && relic.IsAllowed(player.RunState));

    internal static List<EventOption> Pool(AncientEventModel ancient, string name) =>
        ((IEnumerable<EventOption>)Property(ancient, name)).ToList();
    internal static EventOption Option(AncientEventModel ancient, string name) => (EventOption)Property(ancient, name);
    private static object Property(AncientEventModel ancient, string name) =>
        AccessTools.Property(ancient.GetType(), name)?.GetValue(ancient)
        ?? throw new MissingMemberException(ancient.GetType().FullName, name);
}

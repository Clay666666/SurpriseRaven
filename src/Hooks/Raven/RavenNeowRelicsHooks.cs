using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;

namespace SurpriseRaven.Hooks.Raven;

internal static class RavenNeowRelicsHooks
{
    private static readonly Lazy<HashSet<ModelId>> NeowIds = new(() => ModelDb.Event<Neow>()
        .AllPossibleOptions.Select(option => option.Relic).OfType<RelicModel>().Select(relic => relic.Id).ToHashSet());

    internal static bool Contains(ModelId id) => NeowIds.Value.Contains(id);
}

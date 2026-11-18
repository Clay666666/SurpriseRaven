using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestAcquisitionSource()
    {
        // Populate model options directly; never execute Chosen, Obtain or a live event.
        var neow = ModelDb.Event<Neow>().ToMutable();
        var options = (IList<EventOption>)neow.CurrentOptions;
        var gift = ModelDb.Relic<GoldenPearl>().ToMutable();
        var chosen = SourceOption(neow, gift);
        options.Add(chosen);
        Check(!RavenAcquisitionSourceHooks.IsDirectNeowAncient(gift, neow), "Unchosen gift was suppressed.");
        SetChosenFixture(chosen, true);
        Check(RavenAcquisitionSourceHooks.IsDirectNeowAncient(gift, neow), "Direct Neow gift was not identified.");
        Check(!RavenAcquisitionSourceHooks.IsDirectNeowAncient(gift, null), "No event was classified as Neow.");
        Check(!RavenAcquisitionSourceHooks.IsDirectNeowAncient(ModelDb.Relic<GoldenPearl>().ToMutable(), neow),
            "A different instance of a Neow relic was suppressed.");
        Check(!RavenAcquisitionSourceHooks.IsDirectNeowAncient(ModelDb.Relic<TouchOfOrobas>().ToMutable(), neow),
            "A derived ancient reward inherited the direct-gift source.");
        var otherEvent = ModelDb.AllEvents.First(e => e is not Neow).ToMutable();
        ((IList<EventOption>)otherEvent.CurrentOptions).Add(chosen);
        Check(!RavenAcquisitionSourceHooks.IsDirectNeowAncient(gift, otherEvent),
            "Another event's ancient award was suppressed.");

        foreach (var rarity in new[] { RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Shop })
        {
            var derived = ModelDb.AllRelics.First(r => r.Rarity == rarity && !RavenRulesHooks.IsExcluded(r)).ToMutable();
            var option = SourceOption(neow, derived);
            SetChosenFixture(option, true);
            options.Add(option);
            Check(RavenRulesHooks.GrantsOpportunity(derived.Rarity)
                && !RavenAcquisitionSourceHooks.IsDirectNeowAncient(derived, neow),
                $"Non-ancient Neow-room award was suppressed: {rarity}.");
        }
        Check(RavenRulesHooks.GrantsOpportunity(gift.Rarity) && !RavenStakeEligibilityHooks.CanStake(gift),
            "Neow identity restriction should affect staking without changing opportunity rarity rules.");

        int completed = 0, notified = 0;
        bool observedDirect = false;
        var scope = new RavenAcquisitionHooks.AcquisitionScope(() => completed++,
            RavenAcquisitionSourceHooks.IsDirectNeowAncient(gift, neow));
        var source = new TaskCompletionSource<RelicModel>();
        var observed = RavenAcquisitionHooks.ObserveObtain(source.Task,
            _ => { observedDirect = scope.IsDirectNeowAncient; notified++; }, scope);
        options.Clear(); // Simulate event/room context changing while pickup is awaited.
        SetChosenFixture(chosen, false);
        Check(scope.IsDirectNeowAncient && !observed.IsCompleted,
            "Source snapshot was not retained until original completion.");
        var derivedScope = new RavenAcquisitionHooks.AcquisitionScope(() => { },
            RavenAcquisitionSourceHooks.IsDirectNeowAncient(ModelDb.Relic<GoldenPearl>().ToMutable(), neow));
        Check(!derivedScope.IsDirectNeowAncient, "Nested acquisition reused the outer source snapshot.");
        source.SetResult(gift);
        Check(ReferenceEquals(observed.GetAwaiter().GetResult(), gift)
            && observedDirect && notified == 1 && completed == 1,
            "Deferred completion lost its original source or notified more than once.");
        scope.Complete();
        Check(completed == 1, "Source-aware acquisition scope completed twice.");
        int changed = 0;
        var originalPlayer = RavenServiceHooks.CurrentPlayer;
        void OnChanged() => changed++;
        RavenServiceHooks.Changed += OnChanged;
        try
        {
            // Null player proves the direct-gift early return precedes all player data/RNG access.
            RavenServiceHooks.OnObtained(null!, gift, scope.IsDirectNeowAncient);
            Check(changed == 0 && ReferenceEquals(originalPlayer, RavenServiceHooks.CurrentPlayer),
                "Suppressed Neow gift touched the active service or emitted a state change.");
        }
        finally { RavenServiceHooks.Changed -= OnChanged; }
    }

    private static EventOption SourceOption(EventModel eventModel, RelicModel relic) =>
        EventOption.FromRelic(relic, eventModel, null, "INITIAL." + relic.Id.Entry);

    // ILSpy verified the private setter; do not run Chosen or any event callback.
    private static void SetChosenFixture(EventOption option, bool value) =>
        typeof(EventOption).GetProperty(nameof(EventOption.WasChosen))!.SetValue(option, value);
}

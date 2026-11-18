using Godot;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;

namespace SurpriseRaven.Hooks.Raven;

// Invoked only by the existing isolated headless initialization probe.
public static partial class RavenHeadlessTestsHooks
{
    private static int _assertions;
    private static readonly List<string> Cases = [];

    public static object Run()
    {
        if (System.Environment.GetEnvironmentVariable("STS2_SURPRISE_RAVEN_HEADLESS_TEST") != "1"
            || DisplayServer.GetName() != "headless")
            throw new InvalidOperationException("Raven tests require isolated headless mode.");

        _assertions = 0;
        Cases.Clear();
        Case("Excel weights, every outcome boundary and result RNG", TestOutcomeWeights);
        Case("rarity triggers, tiers and targets", TestRarities);
        Case("starter and upgraded starter stakes, rejection and restore", TestStakeEligibility);
        Case("direct Neow gift source, derived awards and deferred snapshot", TestAcquisitionSource);
        Case("native obtained history, Neow exclusion and single-source Ancient pools", TestRelicHistoryAndPools);
        Case("merchant stock and remaining treasure relics", TestNativeRoomRelicOffers);
        Case("pending and nested relic rewards, release and saved claims", TestRelicReservations);
        Case("native Ancient pools, option callbacks and act replacement", TestNativeAncients);
        Case("50 percent lighting and saturation", TestLights);
        Case("independent RNG continuation after JSON save", TestRngRestore);
        Case("0.4.2 RitsuLib player slots and all five RNG streams survive the public rename", TestLegacyPersistenceIdentity);
        Case("111 shuffle vectors and saved reward RNG continuation", TestLegacyShuffle);
        Case("full relic properties and state JSON round trip", TestRelicRestore);
        Case("queued opportunities, exit and room expiration", TestSessions);
        Case("acquisition observer success, failure, suppression and completion", TestAcquisitionObserver);
        Case("locked round results, opportunity identity and legacy migration", TestPendingRounds);
        Case("old Common downgrades display and settle as equal trades", TestLegacyCommonDowngrades);
        Case("drop consumption, explicit pickup and retained-stake rules", TestSacrificeTransactions);
        Case("sacrifice protocol migration, invalidation and command failures", TestSacrificeRecovery);
        Case("independent original and bonus pickup", TestBonusIndependentPickup);
        Case("bonus original identity across restore and removal", TestOriginalIdentity);
        Case("bonus forfeiture, room expiry and deferred opportunities", TestBonusAbandonment);
        Case("abandonment exit, transition reentry and failure cleanup", TestExitCoordination);
        Case("frontend assets intact and decodable from the mounted PCK", TestFrontendAssets);
        Case("round timeline order and frame independence", TestTimeline);
        Case("raven flight travel, wingbeat, smoothness and reduced motion", TestRavenMotion);
        Case("entry probability and independent candle clip choices", TestAudioEvents);
        return new
        {
            status = "passed", assertions = _assertions, cases = Cases.ToArray(),
            display_server = DisplayServer.GetName(),
            scope = "rules, state, serialization; no run or gameplay interaction started"
        };
    }

    private static void Case(string name, Action test)
    {
        try { test(); Cases.Add(name); }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Raven headless test failed: {name}", error);
        }
    }

    private static void Check(bool value, string message)
    {
        _assertions++;
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, message);
    }

    private static void TestRarities()
    {
        var triggers = new[] { RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Ancient, RelicRarity.Shop };
        foreach (var rarity in Enum.GetValues<RelicRarity>())
            Check(RavenRulesHooks.GrantsOpportunity(rarity) == triggers.Contains(rarity), "Incorrect opportunity rarity.");
        Check(RavenRulesHooks.Tier(RelicRarity.Starter) == RelicRarity.Common, "Starter tier differs.");
        Check(RavenRulesHooks.Tier(RelicRarity.None) == RelicRarity.Common, "None tier differs.");
        Check(RavenRulesHooks.Tier(RelicRarity.Shop) == RelicRarity.Uncommon, "Shop tier differs.");
        var ladder = new[] { RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Ancient };
        for (int i = 0; i < ladder.Length; i++)
        {
            Check(RavenRulesHooks.Target(ladder[i], RavenOutcome.Downgrade) == ladder[Math.Max(0, i - 1)],
                "Downgrade target differs.");
            Check(RavenRulesHooks.Target(ladder[i], RavenOutcome.Bonus) == ladder[i], "Bonus is not same tier.");
            if (i < ladder.Length - 1)
                Check(RavenRulesHooks.Target(ladder[i], RavenOutcome.Upgrade) == ladder[i + 1], "Upgrade target differs.");
        }
        Check(RavenRulesHooks.Target(RelicRarity.Shop, RavenOutcome.Upgrade) == RelicRarity.Rare,
            "Shop stake can directly produce Ancient.");
        var eventRelic = ModelDb.AllRelics.First(relic => relic.Rarity == RelicRarity.Event);
        Check(RavenRulesHooks.IsExcluded(eventRelic), "Event relic can be staked.");
    }

    private static void TestLights()
    {
        for (int lights = 0; lights <= 6; lights++)
        {
            for (int roll = 0; roll < 100; roll++)
                Check(RavenRulesHooks.NextLights(lights, roll) == Math.Min(6, lights + (roll < 50 ? 1 : 0)),
                    "Lighting probability or six-light cap differs.");
        }
        Reject(() => RavenRulesHooks.NextLights(0, -1), "Negative lighting roll accepted.");
        Reject(() => RavenRulesHooks.NextLights(0, 100), "Invalid lighting roll accepted.");
    }
}

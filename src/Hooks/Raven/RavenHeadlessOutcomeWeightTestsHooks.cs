using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Random;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestOutcomeWeights()
    {
        var rows = RavenGameplayOddsConfigHooks.Parse(RavenGameplayOddsHooks.ReadPublishedJson()).Outcomes;
        var common = rows["Common"];
        var uncommon = rows["Uncommon"];
        var rare = rows["Rare"];
        var ancient = rows["Ancient"];
        CheckOutcomeTable(RelicRarity.Common, common);
        CheckOutcomeTable(RelicRarity.None, common);
        CheckOutcomeTable(RelicRarity.Uncommon, uncommon);
        CheckOutcomeTable(RelicRarity.Shop, uncommon);
        CheckOutcomeTable(RelicRarity.Rare, rare);
        CheckOutcomeTable(RelicRarity.Ancient, ancient);

        var copy = RavenRulesHooks.GetWeights(0, RelicRarity.Ancient);
        copy[(int)RavenOutcome.Keep] = 0;
        Check(RavenRulesHooks.GetWeights(0, RelicRarity.Ancient).SequenceEqual(ancient[0]),
            "A caller changed the Ancient weight table.");
        var defaultOdds = RavenRulesHooks.GetOdds(0);
        Check(defaultOdds.SequenceEqual(uncommon[0][..5]), "GetOdds no longer returns the default five weights.");
        defaultOdds[0] = 99;
        Check(RavenRulesHooks.GetOdds(0).SequenceEqual(uncommon[0][..5]), "A caller changed the default odds.");
        Check(RavenRulesHooks.DescribeOdds(0) == RavenRulesHooks.DescribeOdds(0, RelicRarity.Shop),
            "The default and Shop odds descriptions should use the Uncommon row.");
    }

    private static void CheckOutcomeTable(RelicRarity rarity, int[][] expected)
    {
        for (int lights = 0; lights < expected.Length; lights++)
        {
            int current = lights;
            var row = expected[lights];
            Check(RavenRulesHooks.GetWeights(lights, rarity).SequenceEqual(row),
                $"{rarity} weights differ from the packaged odds at {lights} lights.");
            int roll = 0;
            for (int outcome = 0; outcome < row.Length; outcome++)
            {
                for (int i = 0; i < row[outcome]; i++, roll++)
                {
                    Check(RavenRulesHooks.Roll(lights, roll, rarity) == (RavenOutcome)outcome,
                        $"{rarity} drew the wrong outcome at {lights} lights, roll {roll}.");
                    if (rarity is RelicRarity.Uncommon or RelicRarity.Ancient)
                        Check(RavenRulesHooks.Roll(lights, roll, rarity == RelicRarity.Ancient) == (RavenOutcome)outcome,
                            $"The boolean overload differs for {rarity}.");
                }
            }
            Reject(() => RavenRulesHooks.Roll(current, -1, rarity), "Negative result roll accepted.");
            Reject(() => RavenRulesHooks.Roll(current, roll, rarity), "Result roll at the weight total accepted.");
            var rng = new Rng(4242UL);
            var reference = new Rng(4242UL);
            int expectedDraw = reference.NextInt(0, roll);
            Check(RavenRulesHooks.Roll(lights, rng, rarity) == RavenRulesHooks.Roll(lights, expectedDraw, rarity)
                && rng.ToSerializable().counter == 1, $"{rarity} did not consume exactly one result roll.");
        }
        Check(RavenRulesHooks.GetWeights(-1, rarity).SequenceEqual(expected[0])
            && RavenRulesHooks.GetWeights(7, rarity).SequenceEqual(expected[6]), "Out-of-range lights are no longer clamped.");
    }
}

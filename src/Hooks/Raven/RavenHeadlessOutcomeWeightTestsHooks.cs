using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Random;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    private static void TestOutcomeWeights()
    {
        // Expected inputs from the approved workbook, in RavenOutcome order.
        int[][] common =
        [
            [5, 40, 55, 0, 0, 0], [7, 48, 45, 0, 0, 0], [9, 54, 37, 0, 0, 0],
            [11, 59, 30, 0, 0, 0], [13, 62, 25, 0, 0, 0], [15, 66, 19, 0, 0, 0],
            [17, 70, 13, 0, 0, 0]
        ];
        int[][] uncommon =
        [
            [6, 40, 52, 2, 0, 0], [8, 44, 42, 6, 0, 0], [10, 50, 32, 8, 0, 0],
            [12, 54, 24, 10, 0, 0], [14, 58, 18, 10, 0, 0], [16, 60, 14, 10, 0, 0],
            [18, 65, 10, 7, 0, 0]
        ];
        int[][] rare =
        [
            [8, 30, 40, 18, 4, 0], [12, 32, 40, 10, 6, 0], [14, 36, 35, 8, 7, 0],
            [16, 40, 31, 5, 8, 0], [18, 42, 26, 5, 9, 0], [20, 45, 20, 5, 10, 0],
            [25, 50, 8, 5, 12, 0]
        ];
        int[][] ancient =
        [
            [0, 0, 0, 30, 6, 64], [0, 0, 0, 32, 8, 60], [0, 0, 0, 34, 8, 58],
            [0, 0, 0, 36, 9, 55], [0, 0, 0, 38, 9, 53], [0, 0, 0, 40, 10, 50],
            [0, 0, 0, 40, 12, 48]
        ];
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
        Check(OddsLine(0, RelicRarity.Common) == "吞噬 5% · 升档 40% · 平换 55%"
            && OddsLine(6, RelicRarity.Rare) == "吞噬 25% · 升档 50% · 平换 8% · 降档 5% · 三选一 12%"
            && OddsLine(6, RelicRarity.Ancient) == "降为稀有 40% · 三选一 12% · 保留原件 48%",
            "The odds descriptions differ from the effective weight rows.");
    }

    private static string OddsLine(int lights, RelicRarity rarity) =>
        RavenRulesHooks.DescribeOdds(lights, rarity).Split('\n')[0];

    private static void CheckOutcomeTable(RelicRarity rarity, int[][] expected)
    {
        for (int lights = 0; lights < expected.Length; lights++)
        {
            int current = lights;
            var row = expected[lights];
            Check(row.Sum() == 100 && RavenRulesHooks.GetWeights(lights, rarity).SequenceEqual(row),
                $"{rarity} weights differ from the workbook at {lights} lights.");
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

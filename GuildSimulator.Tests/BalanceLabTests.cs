using GuildSimulator.Balance;
using GuildSimulator.Core;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Guild static state")]
public class BalanceLabTests
{
    [Fact]
    public void SeedScopeReplaysTheSameRandomSequence()
    {
        int[] first;
        int[] second;
        using (GameRandom.UseSeed(2468))
            first = Enumerable.Range(0, 10).Select(_ => GameRandom.Range(0, 1_000_000)).ToArray();
        using (GameRandom.UseSeed(2468))
            second = Enumerable.Range(0, 10).Select(_ => GameRandom.Range(0, 1_000_000)).ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public void BattleAndQuestScenariosAreDeterministicAndReportUsefulMetrics()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var configuration = new BalanceConfiguration
        {
            seed = 13579,
            runs = 25,
            scenarios =
            {
                new BalanceScenario
                {
                    id = "battle",
                    type = "battle",
                    partyIds = { "adv_0001", "adv_0002", "adv_0003", "adv_0004" },
                    enemyUnitId = "unit_slime_pair",
                },
                new BalanceScenario
                {
                    id = "quest",
                    type = "quest",
                    partyIds = { "adv_0001", "adv_0002", "adv_0003", "adv_0004" },
                    questId = "quest_slime_cull",
                    startingGold = 5000,
                    maxTurns = 20,
                },
            },
        };

        var runner = new BalanceRunner(db);
        var first = runner.Run(configuration);
        var second = runner.Run(configuration);

        Assert.Equal(first.scenarios.Select(x => x.clearRatePercent), second.scenarios.Select(x => x.clearRatePercent));
        Assert.Equal(first.scenarios.Select(x => x.meanRemainingHpPercent), second.scenarios.Select(x => x.meanRemainingHpPercent));
        Assert.Equal(first.scenarios.Select(x => x.meanGoldDelta), second.scenarios.Select(x => x.meanGoldDelta));
        Assert.All(first.scenarios, result => Assert.InRange(result.clearRatePercent, 0, 100));
        Assert.All(first.scenarios, result => Assert.InRange(result.meanRemainingHpPercent, 0, 100));
        Assert.True(first.scenarios.Single(x => x.id == "battle").meanRounds > 0);
        Assert.True(first.scenarios.Single(x => x.id == "quest").meanTurns > 0);
    }

    [Fact]
    public void BaselineComparisonUsesPercentagePointDeltas()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var configuration = new BalanceConfiguration
        {
            seed = 42,
            runs = 5,
            scenarios =
            {
                new BalanceScenario
                {
                    id = "comparison",
                    type = "battle",
                    partyIds = { "adv_0001" },
                    enemyUnitId = "unit_slime_pair",
                },
            },
        };
        var baseline = new BalanceReport
        {
            scenarios =
            {
                new BalanceScenarioResult
                {
                    id = "comparison",
                    winRatePercent = 25,
                    clearRatePercent = 25,
                    meanRemainingHpPercent = 40,
                },
            },
        };

        var result = new BalanceRunner(db).Run(configuration, baseline: baseline).scenarios.Single();

        Assert.NotNull(result.baselineDelta);
        Assert.Equal(result.clearRatePercent - 25, result.baselineDelta!.clearRatePoints, 4);
        Assert.Equal(result.meanRemainingHpPercent - 40, result.baselineDelta.meanRemainingHpPoints, 4);
    }
}

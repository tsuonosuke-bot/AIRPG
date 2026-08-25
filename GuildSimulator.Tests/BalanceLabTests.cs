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
                    partyCapacityUpgrades = 1,
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
                    meanEndingLevel = 1,
                    meanEndingRank = 1,
                },
            },
        };

        var result = new BalanceRunner(db).Run(configuration, baseline: baseline).scenarios.Single();

        Assert.NotNull(result.baselineDelta);
        Assert.Equal(result.clearRatePercent - 25, result.baselineDelta!.clearRatePoints, 4);
        Assert.Equal(result.meanRemainingHpPercent - 40, result.baselineDelta.meanRemainingHpPoints, 4);
        Assert.Equal(result.meanEndingLevel - 1, result.baselineDelta.meanEndingLevel, 4);
        Assert.Equal(result.meanEndingRank - 1, result.baselineDelta.meanEndingRank, 4);
    }

    [Fact]
    public void PartyStateOverridesApplyLevelRankAndEquipment()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var configuration = new BalanceConfiguration
        {
            seed = 777,
            runs = 3,
            scenarios =
            {
                new BalanceScenario
                {
                    id = "prepared-party",
                    type = "battle",
                    party =
                    {
                        new BalancePartyMember
                        {
                            id = "adv_0001",
                            level = 4,
                            rank = 2,
                            equipment = { ["RightHand"] = "eq_sword_02" },
                        },
                        new BalancePartyMember { id = "adv_0002", level = 2, rank = 1 },
                    },
                    enemyUnitId = "unit_slime_pair",
                },
            },
        };

        var result = new BalanceRunner(db).Run(configuration).scenarios.Single();

        Assert.Equal(3, result.meanEndingLevel);
        Assert.Equal(1.5, result.meanEndingRank);
        Assert.Equal(100, result.clearRatePercent);
    }

    [Fact]
    public void CampaignCarriesPartyStateAcrossQuestSteps()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var configuration = new BalanceConfiguration
        {
            seed = 24680,
            runs = 5,
            scenarios =
            {
                new BalanceScenario
                {
                    id = "growth-campaign",
                    type = "campaign",
                    partyIds = { "adv_0001", "adv_0002", "adv_0003", "adv_0004" },
                    partyCapacityUpgrades = 1,
                    partyLevel = 10,
                    questIds = { "quest_slime_cull", "quest_raven_nuisance", "quest_goblin_slayer" },
                    startingGuildRank = 2,
                    maxTurns = 20,
                },
            },
        };

        var result = new BalanceRunner(db).Run(configuration).scenarios.Single();

        Assert.Equal(3, result.campaignSteps.Count);
        Assert.Equal(100, result.campaignSteps[0].reachRatePercent);
        Assert.Equal(100, result.campaignSteps[1].reachRatePercent);
        Assert.True(result.campaignSteps[1].meanStartingLevel >= result.campaignSteps[0].meanEndingLevel);
        Assert.Equal(3, result.meanCompletedSteps);
    }

    [Fact]
    public void CampaignCanAutoPromotePartyAfterFToEProgression()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var configuration = new BalanceConfiguration
        {
            seed = 97531,
            runs = 1,
            scenarios =
            {
                new BalanceScenario
                {
                    id = "promotion-campaign",
                    type = "campaign",
                    partyIds = { "adv_0001", "adv_0002", "adv_0003", "adv_0004" },
                    partyCapacityUpgrades = 1,
                    partyLevel = 20,
                    questIds =
                    {
                        "quest_slime_cull",
                        "quest_raven_nuisance",
                        "quest_wolf_cull",
                        "quest_poison_spider_cull",
                    },
                    startingGuildRank = 2,
                    maxTurns = 30,
                    autoRankUp = true,
                },
            },
        };

        var result = new BalanceRunner(db).Run(configuration).scenarios.Single();

        Assert.Equal(100, result.clearRatePercent);
        Assert.Equal(2, result.meanEndingRank);
        Assert.Equal(1, result.campaignSteps[3].meanStartingRank);
        Assert.Equal(2, result.campaignSteps[3].meanEndingRank);
    }

    [Fact]
    public void InvalidPartyEquipmentIsRejectedBeforeSimulation()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var configuration = new BalanceConfiguration
        {
            runs = 1,
            scenarios =
            {
                new BalanceScenario
                {
                    id = "invalid-equipment",
                    type = "battle",
                    party =
                    {
                        new BalancePartyMember
                        {
                            id = "adv_0001",
                            equipment = { ["Body"] = "eq_sword_02" },
                        },
                    },
                    enemyUnitId = "unit_slime_pair",
                },
            },
        };

        var error = Assert.Throws<InvalidDataException>(() => new BalanceRunner(db).Run(configuration));

        Assert.Contains("cannot be equipped", error.Message);
    }

    [Fact]
    public void DuplicateExplicitFormationSlotsAreRejectedBeforeSimulation()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var configuration = new BalanceConfiguration
        {
            runs = 1,
            scenarios =
            {
                new BalanceScenario
                {
                    id = "duplicate-formation-slot",
                    type = "battle",
                    party =
                    {
                        new BalancePartyMember { id = "adv_0001", formationSlot = 1 },
                        new BalancePartyMember { id = "adv_0002", formationSlot = 1 },
                    },
                    enemyUnitId = "unit_slime_pair",
                },
            },
        };

        var error = Assert.Throws<InvalidDataException>(() =>
            new BalanceRunner(db).Run(configuration));

        Assert.Contains("duplicate formation slots", error.Message);
    }

    [Fact]
    public void QuestScenarioRequiresExplicitPartyCapacityUpgradeForFourthMember()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var configuration = new BalanceConfiguration
        {
            runs = 1,
            scenarios =
            {
                new BalanceScenario
                {
                    id = "locked-fourth-member",
                    type = "quest",
                    partyIds = { "adv_0001", "adv_0002", "adv_0003", "adv_0004" },
                    questId = "quest_slime_cull",
                },
            },
        };

        var error = Assert.Throws<InvalidDataException>(() =>
            new BalanceRunner(db).Run(configuration));

        Assert.Contains("partyCapacityUpgrades=1", error.Message);
    }

    [Fact]
    public void LongCampaignSelectsAnotherSkillRecipientWhenTheFirstAlreadyKnowsIt()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var configuration = new BalanceConfiguration
        {
            seed = 20260803,
            runs = 100,
            scenarios =
            {
                new BalanceScenario
                {
                    id = "repeated-choice-events",
                    type = "campaign",
                    partyIds = { "adv_0001", "adv_0002", "adv_0003" },
                    questIds =
                    {
                        "quest_slime_cull",
                        "quest_raven_nuisance",
                        "quest_goblin_slayer",
                        "quest_wolf_cull",
                        "quest_wolf_pack_hunt",
                        "quest_spider_nest_clear",
                        "quest_promotion_1",
                        "quest_caravan_escort",
                        "quest_poison_spider_cull",
                        "quest_ranpos_cull",
                    },
                    startingGuildRank = 1,
                    maxTurns = 50,
                },
            },
        };

        var result = new BalanceRunner(db).Run(configuration).scenarios.Single();

        Assert.Equal(10, result.campaignSteps.Count);
        Assert.All(result.campaignSteps, step => Assert.InRange(step.reachRatePercent, 0, 100));
    }
}

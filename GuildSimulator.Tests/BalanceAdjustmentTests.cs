using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

public class BalanceAdjustmentTests
{
    [Fact]
    public void SurvivalFirstUsesFiftyPercentPartyBoundary()
    {
        var first = Adventurer("first", hp: 50);
        var second = Adventurer("second", hp: 50);

        Assert.True(BattleResolver.ShouldSurvivalFirstRetreat(new IUnitMember?[] { first, second }));

        first.CombatHp = 51;
        second.CombatHp = 51;

        Assert.False(BattleResolver.ShouldSurvivalFirstRetreat(new IUnitMember?[] { first, second }));
    }

    [Fact]
    public void SurvivalFirstUsesTwentyFivePercentMemberBoundary()
    {
        var endangered = Adventurer("endangered", hp: 25);
        var healthy = Adventurer("healthy", hp: 100);

        Assert.True(BattleResolver.ShouldSurvivalFirstRetreat(new IUnitMember?[] { endangered, healthy }));

        endangered.CombatHp = 26;

        Assert.False(BattleResolver.ShouldSurvivalFirstRetreat(new IUnitMember?[] { endangered, healthy }));
    }

    [Theory]
    [InlineData(1, false, 0, 6)]
    [InlineData(2, false, 0, 12)]
    [InlineData(3, false, 0, 22)]
    [InlineData(2, true, 0, 27)]
    [InlineData(3, true, 10, 27)]
    [InlineData(1, false, 100, 0)]
    public void FatalityRateIncludesSeverityWipeAndInfirmary(
        int severity,
        bool partyWiped,
        int reduction,
        int expected)
    {
        Assert.Equal(expected,
            AdventurerData.CalculateFatalityPercent(severity, partyWiped, reduction));
    }

    [Fact]
    public void ExperienceIsSharedWithoutMultiplyingTheTotal()
    {
        var shares = Enumerable.Range(0, 3)
            .Select(index => ExperienceRewardSplitter.ShareFor(10, 3, index))
            .ToArray();

        Assert.Equal(new[] { 4, 3, 3 }, shares);
        Assert.Equal(10, shares.Sum());
    }

    [Fact]
    public void CurrentQuestExperienceRewardsStayInsideTheSmoothedBands()
    {
        var db = LoadMaster();

        foreach (var quest in db.allQuests)
        {
            var band = RankBandTable.ForQuestRank(quest.rank);
            Assert.NotNull(band);
            Assert.True(band!.RewardExp.Contains(quest.rewardExp),
                $"{quest.id}: rewardExp {quest.rewardExp} is outside {band.RewardExp}");
        }
    }

    [Theory]
    [InlineData("quest_raven_nuisance", 1, 1, "不利")]
    [InlineData("quest_raven_nuisance", 2, 1, "適正")]
    [InlineData("quest_goblin_slayer", 3, 1, "適正")]
    [InlineData("quest_wolf_pack_hunt", 2, 1, "不利")]
    public void PartyAssessmentComparesTheFormationToTheQuest(
        string questId,
        int memberCount,
        int memberRank,
        string expectedLabel)
    {
        var db = LoadMaster();
        var quest = db.allQuests.Single(candidate => candidate.id == questId);
        var members = Enumerable.Range(0, memberCount)
            .Select(index => Adventurer($"member-{index}", hp: 100, rank: memberRank))
            .ToList();

        var assessment = DungeonDifficulty.EvaluateParty(quest, members);

        Assert.Equal(expectedLabel, assessment.Label);
    }

    static GameMasterData LoadMaster()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        return MasterLoader.Load(dataDir);
    }

    static AdventurerData Adventurer(string id, int hp, int rank = Rank.Min)
    {
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = id,
            baseName = id,
            defaultLevel = 1,
            defaultRank = rank,
            vitality = 10,
            mental = 10,
            strength = 10,
            agility = 10,
            intelligence = 10,
            constitution = 10,
        })
        {
            rank = rank,
            CombatHpMax = 100,
            CombatHp = hp,
        };
        return adventurer;
    }
}

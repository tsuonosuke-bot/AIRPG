using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using Xunit;

namespace GuildSimulator.Tests;

public class QuestRewardTests
{
    [Theory]
    [InlineData(8, 30)]
    [InlineData(9, 40)]
    [InlineData(12, 70)]
    public void GatherQuestPaysBaseRewardPlusSurplusOnly(int gatheredCount, int expectedGold)
    {
        var definition = new QuestMasterData
        {
            id = "gather",
            questName = "月光草の採取",
            rewardGold = 30,
            gatherItemName = "月光草",
            gatherTargetCount = 8,
            gatherGoldPerItem = 10,
        };
        var run = new QuestRun(definition, startedTurn: 1)
        {
            gatheredCount = gatheredCount,
        };
        var guild = new GuildManager(startGold: 0);

        new QuestRewardService().ApplyBaseRewards(run, guild, "[報酬]");

        Assert.Equal(expectedGold, guild.Gold);
    }
}

using GuildSimulator.Core;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using Xunit;

namespace GuildSimulator.Tests;

public class RankedTreasureRewardTests
{
    [Fact]
    public void RewardRankRangeDefaultsToAllQuestRanksAndSurvivesCopy()
    {
        var source = new RewardEntryData
        {
            type = RewardType.Gold,
            gold = 10,
        };

        Assert.Equal(Rank.Min, source.minQuestRank);
        Assert.Equal(Rank.Max, source.maxQuestRank);

        var copy = source.Copy();

        Assert.Equal(source.minQuestRank, copy.minQuestRank);
        Assert.Equal(source.maxQuestRank, copy.maxQuestRank);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 30)]
    public void DungeonChestOnlyUsesEntriesEligibleForTheQuestRank(int questRank, int expectedGold)
    {
        var dungeon = new DungeonMasterData
        {
            id = "ranked-treasure",
            treasureTable =
            {
                Gold(10, minRank: 1, maxRank: 1),
                Gold(20, minRank: 2, maxRank: 2),
                Gold(30, minRank: 3, maxRank: 3),
            },
        };
        var run = ChestRun(dungeon, questRank, useKey: true);

        new QuestRewardService().OpenChests(run, new GuildManager(startGold: 0), "[完了]");

        Assert.Equal(expectedGold, Assert.Single(run.pendingLoot).gold);
    }

    [Fact]
    public void DungeonChestIsEmptyWhenNoEntryMatchesTheQuestRank()
    {
        var dungeon = new DungeonMasterData
        {
            id = "ranked-treasure",
            treasureTable =
            {
                Gold(20, minRank: 2, maxRank: 2),
            },
        };
        var run = ChestRun(dungeon, questRank: 1, useKey: true);

        new QuestRewardService().OpenChests(run, new GuildManager(startGold: 0), "[完了]");

        Assert.Empty(run.pendingLoot);
        Assert.Contains(run.logs, log => log.Contains("空っぽだった"));
    }

    [Theory]
    [InlineData(14, true)]  // Random.NextDouble() = 0.040...: 10%未満なので空。
    [InlineData(18, false)] // Random.NextDouble() = 0.129...: 10%以上なので中身が出る。
    public void DungeonChestUsesTenPercentEmptyRate(int seed, bool expectedEmpty)
    {
        Assert.Equal(0.1f, QuestRewardService.EmptyChestRate);

        var dungeon = new DungeonMasterData
        {
            id = "empty-rate",
            treasureTable =
            {
                Gold(10, minRank: Rank.Min, maxRank: Rank.Max),
            },
        };
        var run = ChestRun(dungeon, questRank: Rank.Min, useKey: false);

        using (GameRandom.UseSeed(seed))
            new QuestRewardService().OpenChests(run, new GuildManager(startGold: 0), "[完了]");

        Assert.Equal(expectedEmpty, run.pendingLoot.Count == 0);
    }

    static RewardEntryData Gold(int amount, int minRank, int maxRank) => new()
    {
        type = RewardType.Gold,
        gold = amount,
        weight = 1,
        minQuestRank = minRank,
        maxQuestRank = maxRank,
    };

    static QuestRun ChestRun(DungeonMasterData dungeon, int questRank, bool useKey)
    {
        var run = new QuestRun(new QuestMasterData
        {
            id = "ranked-treasure-quest",
            rank = questRank,
            Dungeon = dungeon,
        }, startedTurn: 1)
        {
            guaranteedNonEmptyChestCount = useKey ? 1 : 0,
        };
        run.chests.Add(new TreasureChest
        {
            kind = TreasureChestKind.Dungeon,
            foundPhase = 1,
        });
        return run;
    }
}

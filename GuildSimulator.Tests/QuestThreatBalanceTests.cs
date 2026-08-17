using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>敵ランク、出現深度、クエスト帯を同じ物差しで固定する。</summary>
public class QuestThreatBalanceTests
{
    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void EveryMonsterAppearsNoEarlierThanOneRankBelowItsThreat()
    {
        var db = Load();

        foreach (var enemy in db.enemies.Values)
        {
            var reachableRanks = new List<int>();
            foreach (var quest in db.allQuests)
            {
                if (quest.BossEnemy?.Formation.Any(member => member?.id == enemy.id) == true)
                    reachableRanks.Add(quest.rank);

                if (quest.Dungeon == null) continue;
                bool appearsNormally = quest.Dungeon.encounterTable.Any(entry =>
                    entry.Unit?.Formation.Any(member => member?.id == enemy.id) == true
                    && entry.minPhase <= quest.totalPhases);
                if (appearsNormally) reachableRanks.Add(quest.rank);
            }

            Assert.NotEmpty(reachableRanks);
            int earliestRank = reachableRanks.Min();
            Assert.True(earliestRank >= enemy.threat - 1,
                $"{enemy.id}（{Rank.Label(enemy.threat)}）が{Rank.Label(earliestRank)}クエストに早すぎる配置です");
        }
    }

    [Fact]
    public void EveryRankHasNormalWorkAndEveryPromotionStepExists()
    {
        var db = Load();

        for (int rank = Rank.Min; rank <= Rank.Max; rank++)
        {
            Assert.Contains(db.allQuests, quest => quest.rank == rank && !quest.isEmergencyQuest);
            if (rank < Rank.Max)
                Assert.Contains(db.allQuests, quest =>
                    quest.rank == rank && quest.isEmergencyQuest && quest.rankUpOnClear == 1);
        }
    }

    [Fact]
    public void QuestRewardsAndLengthsStayInsideTheirRankBands()
    {
        var db = Load();

        foreach (var quest in db.allQuests)
        {
            var band = RankBandTable.ForQuestRank(quest.rank)!;
            Assert.True(band.RewardGold.Contains(quest.rewardGold),
                $"{quest.id}: Gold {quest.rewardGold} / {band.RewardGold}");
            Assert.True(band.RewardExp.Contains(quest.rewardExp),
                $"{quest.id}: EXP {quest.rewardExp} / {band.RewardExp}");
            Assert.True(band.GuildPoints.Contains(quest.rewardGuildPoints),
                $"{quest.id}: GP {quest.rewardGuildPoints} / {band.GuildPoints}");
            Assert.True(band.TotalPhases.Contains(quest.totalPhases),
                $"{quest.id}: phases {quest.totalPhases} / {band.TotalPhases}");
        }
    }

    [Fact]
    public void BossesAndNormalEncountersNeverJumpMoreThanOneRank()
    {
        var db = Load();

        foreach (var quest in db.allQuests)
        {
            var rating = DungeonDifficulty.Evaluate(quest);
            Assert.True(rating.enemyThreatMax <= quest.rank + 1,
                $"{quest.id}: 通常遭遇 最大{Rank.Label(rating.enemyThreatMax)} / クエスト{Rank.Label(quest.rank)}");
            if (rating.hasBoss)
                Assert.True(rating.bossThreat <= quest.rank + 1,
                    $"{quest.id}: ボス{Rank.Label(rating.bossThreat)} / クエスト{Rank.Label(quest.rank)}");
        }
    }

    [Fact]
    public void WarningKeepsTypicalThreatSeparateFromRareMaximumThreat()
    {
        var db = Load();
        var quest = db.allQuests.Single(candidate => candidate.id == "quest_promotion_4");

        var rating = DungeonDifficulty.Evaluate(quest);

        Assert.True(rating.enemyThreatTypical < rating.enemyThreatMax);
        Assert.Equal(5, rating.enemyThreatMax);
        Assert.InRange(rating.enemyThreatMaxEncounterChancePercent, 0.1f, 99.9f);
    }
}

using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// 育成フィードバックで求められた、成長の可視性・過積載の説明可能性・
/// 新人向け依頼枠を固定する回帰テスト。
/// </summary>
[Collection("Guild static state")]
public sealed class ProgressionFeedbackTests
{
    [Fact]
    public void LevelUpHistoryNamesTheAbilityAndItsGain()
    {
        // FacilitySystem は static なので、訓練所なしのギルドで状態を明示的に初期化する。
        _ = new GuildManager();
        var adventurer = Adventurer("growth", "成長確認");
        int levelBefore = adventurer.level;

        Assert.True(adventurer.AddExperience(
            adventurer.RequiredExpForNextLevel,
            out int levelUps,
            out var grownStats));

        Assert.Equal(1, levelUps);
        var grownStat = Assert.Single(grownStats);
        string history = Assert.Single(adventurer.adventureHistory);
        Assert.Contains($"Lv{levelBefore}→{levelBefore + 1}", history);
        Assert.Contains($"{AdventurerData.StatDisplayName(grownStat)}+1", history);
    }

    [Fact]
    public void QuestRewardRecordsTheAbilityGrowthForTheCompletionReport()
    {
        var guild = new GuildManager();
        var adventurer = Adventurer("quest-growth", "帰還成長確認");
        var run = new QuestRun(new QuestMasterData
        {
            id = "growth-report",
            rewardExp = adventurer.RequiredExpForNextLevel,
        }, startedTurn: 1);
        run.formation[0] = adventurer;

        new QuestRewardService().ApplyBaseRewards(run, guild, "[完了]");

        var grownStat = Assert.Single(run.levelGrowthsByAdventurerId[adventurer.id]);
        Assert.Equal(2, adventurer.level);
        Assert.Contains(
            $"{AdventurerData.StatDisplayName(grownStat)}+1",
            QuestManager.FormatGrownStats(run.levelGrowthsByAdventurerId[adventurer.id]));
    }

    [Fact]
    public void PublishedOverweightPenaltiesMatchFinalCombatStats()
    {
        _ = new GuildManager();
        var adventurer = Adventurer("overweight", "過積載確認",
            vitality: 6, strength: 6, constitution: 4);
        adventurer.SetEquipped(EquipSlot.RightHand, new EquipmentMasterData
        {
            id = "heavy_test_weapon",
            displayName = "試験用重量武器",
            type = EquipmentType.Weapon,
            weight = 14,
        });

        var baseStats = adventurer.GetBaseCombatStats();
        var finalStats = adventurer.GetFinalCombatStats();

        Assert.Equal(10, adventurer.CarryLimit);
        Assert.Equal(14, adventurer.TotalEquipmentWeight);
        Assert.Equal(4, adventurer.OverweightAmount);
        Assert.Equal(2, adventurer.OverweightToHitPenalty);
        Assert.Equal(3, adventurer.OverweightDvPenalty);
        Assert.Equal(baseStats.toHit - adventurer.OverweightToHitPenalty, finalStats.toHit);
        Assert.Equal(baseStats.dv - adventurer.OverweightDvPenalty, finalStats.dv);
    }

    [Fact]
    public void NoviceFacilityKeepsOneFRankQuestInASeparateSlotAtHighGuildRank()
    {
        var guild = new GuildManager(startGold: 0, startRank: Rank.Max);
        var trainingHall = new FacilityMasterData
        {
            id = "test_training_hall",
            displayName = "試験用訓練所",
            requiredGuildRank = Rank.Min,
            noviceQuestBoardBonus = 1,
        };
        Assert.True(guild.TryBuildFacility(trainingHall, out var reason), reason);

        var manager = new QuestManager(guild);
        var quests = new List<QuestMasterData>
        {
            Quest("f_training", Rank.Min),
            Quest("s_1", Rank.Max),
            Quest("s_2", Rank.Max),
            Quest("s_3", Rank.Max),
            Quest("s_4", Rank.Max),
            Quest("s_5", Rank.Max),
        };

        manager.FillBoard(quests, currentTurn: 1);

        var normalEntries = manager.questBoard
            .Where(entry => !entry.quest.isEmergencyQuest)
            .ToList();
        Assert.Equal(manager.BaseNormalBoardCapacity, manager.NormalBoardCapacity);
        Assert.Equal(1, manager.NoviceBoardCapacity);
        Assert.Equal(manager.NormalBoardCapacity + manager.NoviceBoardCapacity, normalEntries.Count);
        Assert.Single(normalEntries, entry => entry.quest.rank == Rank.Min);
        Assert.All(normalEntries.Where(entry => entry.quest.rank != Rank.Min),
            entry => Assert.Equal(Rank.Max, entry.quest.rank));
        Assert.Equal(
            manager.NormalBoardCapacity + manager.NoviceBoardCapacity + manager.EmergencyBoardCapacity,
            manager.BoardCapacity);
    }

    static AdventurerData Adventurer(
        string id,
        string name,
        int vitality = 10,
        int strength = 10,
        int constitution = 10) =>
        new(new AdventurerMasterData
        {
            id = id,
            baseName = name,
            defaultLevel = 1,
            defaultRank = Rank.Min,
            vitality = vitality,
            mental = 10,
            strength = strength,
            agility = 10,
            intelligence = 10,
            constitution = constitution,
            appearance = 10,
        });

    static QuestMasterData Quest(string id, int rank) => new()
    {
        id = id,
        questName = id,
        rank = rank,
    };
}

using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>草原・森の追加と、報酬/イベント再調整を一つの受け入れ条件として固定する。</summary>
[Collection("Guild static state")]
public class FeedbackBalanceTests
{
    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void GrasslandAndForestMonstersHaveNamesLoreAndReachableUnits()
    {
        var db = Load();

        Assert.Equal("コルヴス", db.enemies["enemy_raven"].baseName);
        Assert.Contains("ずる賢い", db.enemies["enemy_raven"].description);
        Assert.Equal("アルミラージ", db.enemies["enemy_horned_rabbit"].baseName);
        Assert.Contains("一本角", db.enemies["enemy_horned_rabbit"].description);

        var aptos = db.enemies["enemy_aptos"];
        Assert.Equal(2, aptos.threat);
        Assert.Contains("家畜化", aptos.description);
        Assert.Contains("臆病", aptos.description);

        var hopper = db.enemies["enemy_forest_hopper"];
        Assert.Equal(2, hopper.threat);
        Assert.Contains("肉食", hopper.description);

        AssertEnemyInBand(aptos, RankBandTable.ForThreat(2)!);
        AssertEnemyInBand(hopper, RankBandTable.ForThreat(2)!);

        var aptosUnit = db.enemyUnits["unit_aptos_herd"];
        Assert.Equal(3, aptosUnit.Formation.Take(3).Count(enemy => enemy?.id == aptos.id));
        var meadowPlacement = db.dungeons["dungeon_meadow"].encounterTable
            .Single(entry => entry.unitId == aptosUnit.id);
        Assert.Equal((2, 11, 0), (meadowPlacement.weight, meadowPlacement.minPhase, meadowPlacement.maxPhase));

        var hopperUnit = db.enemyUnits["unit_forest_hopper_pair"];
        Assert.Equal(2, hopperUnit.Formation.Take(3).Count(enemy => enemy?.id == hopper.id));
        var forestPlacement = db.dungeons["dungeon_woods"].encounterTable
            .Single(entry => entry.unitId == hopperUnit.id);
        Assert.Equal((2, 11, 15), (forestPlacement.weight, forestPlacement.minPhase, forestPlacement.maxPhase));
    }

    [Fact]
    public void CurrentQuestGoldUsesTheSmoothedFToDBands()
    {
        var db = Load();

        foreach (var quest in db.allQuests.Where(quest => quest.rank <= 3))
        {
            var band = RankBandTable.ForQuestRank(quest.rank)!;
            Assert.True(band.RewardGold.Contains(quest.rewardGold),
                $"{quest.id}: {quest.rewardGold}G is outside {band.RewardGold}");
        }

        var perTurnMedians = Enumerable.Range(1, 3).ToDictionary(
            rank => rank,
            rank => Median(db.allQuests
                .Where(quest => quest.rank == rank)
                .Select(quest => quest.rewardGold
                    / (double)Math.Max(1, (int)Math.Ceiling(
                        quest.totalPhases / (double)quest.phasesPerTurn)))));
        Assert.True(perTurnMedians[2] / perTurnMedians[1] <= 2.5,
            $"F→Eの1ターン当たり中央値が急すぎます: {perTurnMedians[1]:0.0}G→{perTurnMedians[2]:0.0}G");
        Assert.True(perTurnMedians[3] / perTurnMedians[2] <= 2.5,
            $"E→Dの1ターン当たり中央値が急すぎます: {perTurnMedians[2]:0.0}G→{perTurnMedians[3]:0.0}G");
    }

    [Fact]
    public void EveryBossOffersARealNonGoldRarePathAndUsesSmallGoldDrops()
    {
        var db = Load();
        var bossQuests = db.allQuests.Where(quest => quest.BossEnemy != null).ToList();
        Assert.NotEmpty(bossQuests);

        foreach (var quest in bossQuests)
        {
            var questRareRewards = quest.bossDrops.Where(IsUncommonOrBetter).ToList();
            var enemyRareRewards = quest.BossEnemy!.Formation
                .Where(enemy => enemy != null)
                .SelectMany(enemy => enemy!.dropTable)
                .Where(IsUncommonOrBetter)
                .ToList();
            Assert.True(questRareRewards.Count + enemyRareRewards.Count > 0,
                $"{quest.id}: Gold以外のUncommon以上ドロップ経路がありません");

            int goldCap = quest.rank switch { 1 => 60, 2 => 120, _ => 200 };
            Assert.All(quest.bossDrops.Where(drop => drop.type == RewardType.Gold),
                drop => Assert.InRange(drop.gold, 1, goldCap));
        }

        Assert.DoesNotContain(bossQuests, quest => quest.bossDropsAreGuaranteed);
    }

    [Fact]
    public void SharedDungeonsOfferRankedGoldAndConsumables()
    {
        var db = Load();

        foreach (var quest in db.allQuests.Where(quest => quest.rank <= 3 && quest.Dungeon != null))
        {
            var eligible = quest.Dungeon!.treasureTable
                .Where(entry => entry.minQuestRank <= quest.rank && quest.rank <= entry.maxQuestRank)
                .ToList();
            Assert.Contains(eligible, entry => entry.type == RewardType.Gold);
            Assert.Contains(eligible, entry => entry.type == RewardType.Consumable);
            Assert.Contains(eligible, entry => entry.type == RewardType.Equipment
                && entry.Equipment?.shopTier >= Math.Min(3, quest.rank));

            var goldValues = eligible.Where(entry => entry.type == RewardType.Gold).Select(entry => entry.gold).ToList();
            int expectedMinimum = quest.rank switch { 1 => 10, 2 => 25, _ => 50 };
            Assert.True(goldValues.Min() >= expectedMinimum,
                $"{quest.id}: 宝箱Goldの最低値が{expectedMinimum}G未満です");
        }
    }

    [Fact]
    public void DeterministicSkillEventsHaveMoreWeightAndCoverAmashiro()
    {
        var db = Load();
        string[] eventIds = { "event_forest_lore", "event_roadside_lessons", "event_ruin_tablets" };

        foreach (string eventId in eventIds)
        {
            var choiceEvent = db.choiceEvents[eventId];
            Assert.True(choiceEvent.weight >= 8, $"{eventId}: weight={choiceEvent.weight}");
            Assert.Equal(3, choiceEvent.options.Count);
            Assert.All(choiceEvent.options, option =>
                Assert.Equal(QuestChoiceEffectType.AdventurerSkill, option.effectType));
        }

        Assert.Contains(db.dungeons["dungeon_amashiro"].turnEndEvents,
            choiceEvent => choiceEvent.id == "event_roadside_lessons");
    }

    [Theory]
    [InlineData(1, 20, 20)]
    [InlineData(2, 20, 40)]
    [InlineData(3, 20, 80)]
    public void ChoiceEventGoldScalesWithQuestRank(int rank, int value, int expected)
    {
        var option = new QuestChoiceOptionData
        {
            text = "資金を動かす",
            resultText = "結果",
            effectType = QuestChoiceEffectType.Gold,
            value = value,
        };
        var choiceEvent = new QuestChoiceEventMasterData { id = "gold_event", options = { option } };
        var run = new QuestRun(new QuestMasterData { id = "q", rank = rank }, startedTurn: 1)
        {
            pendingChoice = new PendingQuestChoice { Event = choiceEvent },
        };

        var manager = new QuestManager(new GuildManager(startGold: 0));
        Assert.True(manager.ResolveChoice(run, 0, out var result), result);
        Assert.Contains(run.pendingLoot,
            reward => reward.type == RewardType.Gold && reward.gold == expected);
        Assert.Contains($"{expected:+#;-#;0}", result);
    }

    [Fact]
    public void DeterministicGoldLossIsPaidImmediatelyAndSurvivesQuestFailure()
    {
        var option = GoldOption(-20);
        var run = PendingGoldRun(rank: 3, option);
        var guild = new GuildManager(startGold: 81);
        var manager = new QuestManager(guild);

        Assert.True(manager.ResolveChoice(run, 0, out var result), result);

        Assert.Equal(1, guild.Gold);
        Assert.Empty(run.pendingLoot);
        Assert.Null(run.pendingChoice);
        Assert.Contains("ゴールド-80", result);
        Assert.Contains("即時支払い", result);
        Assert.Contains(guild.economyLogs, log => log.Contains("-80G"));

        run.failed = true;
        manager.FinalizeQuest(run);
        Assert.Equal(1, guild.Gold);
    }

    [Theory]
    [InlineData(79, "資金が不足")]
    [InlineData(80, "支払い後に0G")]
    public void DeterministicGoldLossKeepsChoicePendingWhenItCannotLeaveOneGold(
        int startingGold,
        string expectedMessage)
    {
        var option = GoldOption(-20);
        var run = PendingGoldRun(rank: 3, option);
        var guild = new GuildManager(startGold: startingGold);
        var manager = new QuestManager(guild);

        Assert.False(manager.ResolveChoice(run, 0, out var result));

        Assert.Contains(expectedMessage, result);
        Assert.Equal(startingGold, guild.Gold);
        Assert.Empty(run.pendingLoot);
        Assert.NotNull(run.pendingChoice);
    }

    [Fact]
    public void RandomGoldLossTakesOnlySpendableGoldAndCannotBeRerolled()
    {
        var option = new QuestChoiceOptionData
        {
            text = "顔役と話をつける",
            resultText = "交渉の結果が出た",
            outcomes =
            {
                new QuestChoiceOutcome
                {
                    weight = 1,
                    effectType = QuestChoiceEffectType.Gold,
                    value = -80,
                    resultText = "身ぐるみを剥がされた",
                },
                new QuestChoiceOutcome
                {
                    weight = 1,
                    effectType = QuestChoiceEffectType.Gold,
                    value = -80,
                    resultText = "路銀を奪われた",
                },
            },
        };
        var run = PendingGoldRun(rank: 2, option);
        var guild = new GuildManager(startGold: 50);
        var manager = new QuestManager(guild);

        Assert.True(manager.ResolveChoice(run, 0, out var result), result);

        Assert.Equal(1, guild.Gold);
        Assert.Empty(run.pendingLoot);
        Assert.Null(run.pendingChoice);
        Assert.Contains("要求 160G", result);
        Assert.Contains("ゴールド-49", result);
        Assert.False(manager.ResolveChoice(run, 0, out _));
    }

    static QuestChoiceOptionData GoldOption(int value) => new()
    {
        text = "資金を動かす",
        resultText = "結果",
        effectType = QuestChoiceEffectType.Gold,
        value = value,
    };

    static QuestRun PendingGoldRun(int rank, QuestChoiceOptionData option)
    {
        var choiceEvent = new QuestChoiceEventMasterData { id = "gold_event", options = { option } };
        return new QuestRun(new QuestMasterData { id = "q", rank = rank }, startedTurn: 1)
        {
            pendingChoice = new PendingQuestChoice { Event = choiceEvent },
        };
    }

    static bool IsUncommonOrBetter(RewardEntryData reward) => reward.type switch
    {
        RewardType.Equipment => reward.Equipment?.rarity >= Rarity.Uncommon,
        RewardType.Consumable => reward.Consumable?.rarity >= Rarity.Uncommon,
        RewardType.Skill => reward.Skill != null,
        _ => false,
    };

    static void AssertEnemyInBand(EnemyMasterData master, RankBandTable.EnemyBand band)
    {
        var enemy = new EnemyData(master);
        var stats = enemy.GetBaseCombatStats() + enemy.GetEquipmentBonus();
        int pv = enemy.WeaponBasePv + Math.Min(enemy.AttackStatModifier, enemy.MaxStatBonus);
        Assert.True(band.Hp.Contains(stats.hp), $"{master.id}: HP {stats.hp}");
        Assert.True(band.Av.Contains(stats.av), $"{master.id}: AV {stats.av}");
        Assert.True(band.Dv.Contains(stats.dv), $"{master.id}: DV {stats.dv}");
        Assert.True(band.Pv.Contains(pv), $"{master.id}: PV {pv}");
        Assert.True(band.Exp.Contains(master.exp), $"{master.id}: EXP {master.exp}");
    }

    static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2d;
    }
}

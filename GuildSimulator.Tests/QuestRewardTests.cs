using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
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

    [Fact]
    public void BossDefeatYieldsAnUnopenedChestOpenedOnReturn()
    {
        var relic = new RelicMasterData { id = "relic", relicName = "試練の証" };
        var run = DefeatBoss(new()
        {
            new() { type = RewardType.Gold, gold = 300, chance = 1f },
            new() { type = RewardType.Relic, relicId = relic.id, Relic = relic, chance = 1f },
        });

        // 撃破した時点では未開封の宝箱があるだけで、中身はまだ決まっていない。
        var chest = Assert.Single(run.chests);
        Assert.True(chest.IsBossChest);
        Assert.Empty(run.pendingLoot);

        var guild = new GuildManager(startGold: 0);
        new QuestRewardService().OpenChests(run, guild, "[完了]");
        new QuestRewardService().ApplyPendingLoot(run, guild, "[完了]");

        Assert.Empty(run.chests);
        Assert.Equal(300, guild.Gold);
        Assert.Contains(relic, guild.relics);
        Assert.Contains(run.logs, log => log.Contains("ボスの宝箱を開けた"));
    }

    [Fact]
    public void BossChestIsNeverEmptiedByTheEmptyRoll()
    {
        var guild = new GuildManager(startGold: 0);
        var service = new QuestRewardService();

        // 空っぽ抽選を受けるなら200回のうち何度かは空になる確率がほぼ1。
        for (int i = 0; i < 200; i++)
        {
            var run = DefeatBoss(new()
            {
                new() { type = RewardType.Gold, gold = 10, chance = 1f },
            });
            service.OpenChests(run, guild, "[完了]");
            Assert.Single(run.pendingLoot);
        }
    }

    [Fact]
    public void BossDropWithoutChanceNeverDrops()
    {
        var run = DefeatBoss(new()
        {
            new() { type = RewardType.Gold, gold = 300, chance = 0f },
        });

        new QuestRewardService().OpenChests(run, new GuildManager(startGold: 0), "[完了]");

        Assert.Empty(run.pendingLoot);
        Assert.Contains(run.logs, log => log.Contains("空っぽだった"));
    }

    [Fact]
    public void GuaranteedBossDropsSkipTheRoll()
    {
        var run = DefeatBoss(
            new() { new() { type = RewardType.Gold, gold = 300, chance = 0f } },
            guaranteed: true);

        new QuestRewardService().OpenChests(run, new GuildManager(startGold: 0), "[完了]");

        Assert.Equal(300, Assert.Single(run.pendingLoot).gold);
    }

    [Fact]
    public void TreasureSkipsRelicsTheGuildAlreadyOwns()
    {
        var owned = new RelicMasterData { id = "relic", relicName = "所持済みの遺物" };
        var dungeon = new DungeonMasterData { id = "dungeon", dungeonName = "試験場" };
        dungeon.treasureTable.Add(new RewardEntryData
        {
            type = RewardType.Relic, relicId = owned.id, Relic = owned, weight = 10,
        });

        var definition = new QuestMasterData
        {
            id = "treasure_quest",
            questName = "宝探し",
            totalPhases = 1,
            bossPhase = 0,
            dungeonId = dungeon.id,
            Dungeon = dungeon,
            fixedEvents = { new QuestPhaseEvent { phase = 1, type = QuestEventType.ForceTreasure } },
        };
        var service = new QuestRewardService();

        // 所持済みなら候補から外れるので、何度開けても出てこない。
        var owner = new GuildManager(startGold: 0);
        owner.AddRelic(owned, "先の依頼");
        var ownerRun = FindChests(definition, 30);
        service.OpenChests(ownerRun, owner, "[完了]");
        Assert.Empty(ownerRun.pendingLoot);

        // 持っていないギルドなら、空っぽ抽選をすり抜けたぶんが出る。
        var newcomer = new GuildManager(startGold: 0);
        var newcomerRun = FindChests(definition, 30);
        service.OpenChests(newcomerRun, newcomer, "[完了]");
        Assert.Contains(newcomerRun.pendingLoot, e => e.Relic == owned);
    }

    [Fact]
    public void DungeonChestsAreSometimesEmptyButNotAlways()
    {
        var dungeon = new DungeonMasterData { id = "dungeon", dungeonName = "試験場" };
        dungeon.treasureTable.Add(new RewardEntryData
        {
            type = RewardType.Gold, gold = 10, weight = 1,
        });
        var definition = new QuestMasterData
        {
            id = "treasure_quest", questName = "宝探し", totalPhases = 1, bossPhase = 0,
            dungeonId = dungeon.id, Dungeon = dungeon,
            fixedEvents = { new QuestPhaseEvent { phase = 1, type = QuestEventType.ForceTreasure } },
        };

        const int chestCount = 400;
        var run = FindChests(definition, chestCount);
        Assert.Equal(chestCount, run.chests.Count);

        new QuestRewardService().OpenChests(run, new GuildManager(startGold: 0), "[完了]");

        // 空っぽ率は2割。400個も開ければ全部当たり／全部ハズレは事実上起きない。
        int opened = run.pendingLoot.Count;
        Assert.InRange(opened, 1, chestCount - 1);
        Assert.Contains(run.logs, log => log.Contains("空っぽだった"));
    }

    // 宝箱イベントだけが起きるクエストを回して、未開封の宝箱を count 個ためる。
    static QuestRun FindChests(QuestMasterData definition, int count)
    {
        var run = new QuestRun(definition, startedTurn: 1) { morale = new MoraleState(999) };
        var progressor = new QuestProgressor();
        for (int i = 0; i < count; i++)
        {
            run.currentPhase = 0;
            progressor.AdvanceOnePhase(run, currentTurn: 1);
        }
        Assert.Equal(count, run.chests.Count);
        Assert.Empty(run.pendingLoot);
        return run;
    }

    [Fact]
    public void TreasureChoiceHandsOutUnopenedChests()
    {
        var guild = new GuildManager(startGold: 0);
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "adv", baseName = "テスト",
            vitality = 10, mental = 10, strength = 10,
            agility = 10, intelligence = 10, constitution = 10,
        });
        guild.AddAdventurer(adventurer);

        var choice = new QuestChoiceEventMasterData
        {
            id = "event", title = "隠された物資庫", weight = 1,
            options =
            {
                new QuestChoiceOptionData
                {
                    text = "奥まで探る", resultText = "運び出した。",
                    effectType = QuestChoiceEffectType.Treasure, value = 2,
                },
                new QuestChoiceOptionData
                {
                    text = "去る", resultText = "立ち去った。",
                    effectType = QuestChoiceEffectType.Morale, value = 5,
                },
            },
        };
        var dungeon = new DungeonMasterData { id = "dungeon", turnEndEventChance = 1f };
        dungeon.turnEndEvents.Add(choice);
        dungeon.treasureTable.Add(new RewardEntryData
        {
            type = RewardType.Gold, gold = 40, weight = 1,
        });

        var quest = new QuestMasterData
        {
            id = "q", totalPhases = 10, phasesPerTurn = 1, Dungeon = dungeon,
        };
        var manager = new QuestManager(guild);
        var formation = new AdventurerData?[6];
        formation[0] = adventurer;
        Assert.True(manager.TryStartQuest(quest, formation, 1, out _));

        manager.AdvanceAll(2);
        var run = manager.activeQuests.Single();
        Assert.True(manager.HasPendingChoices);
        Assert.True(manager.ResolveChoice(run, 0, out var result));

        // 選択した時点では未開封のまま。中身が決まるのは帰還後。
        Assert.Equal(2, run.chests.Count);
        Assert.All(run.chests, chest => Assert.False(chest.IsBossChest));
        Assert.Empty(run.pendingLoot);
        Assert.Contains("宝箱 x2", result);
    }

    // ボスを必ず倒せる編成で bossPhase を1フェーズだけ進め、ドロップ抽選の結果を返す。
    static QuestRun DefeatBoss(List<RewardEntryData> bossDrops, bool guaranteed = false)
    {
        var boss = new EnemyUnitTemplate { id = "boss", unitName = "案山子" };
        boss.Formation.Add(new EnemyMasterData { id = "boss_body", baseName = "案山子" });
        while (boss.Formation.Count < 6) boss.Formation.Add(null);

        var definition = new QuestMasterData
        {
            id = "boss_quest",
            questName = "ボス討伐",
            totalPhases = 1,
            bossPhase = 1,
            bossEnemyId = boss.id,
            BossEnemy = boss,
            bossDropsAreGuaranteed = guaranteed,
            bossDrops = bossDrops,
        };

        var hero = new AdventurerData(new AdventurerMasterData
        {
            id = "hero",
            baseName = "英雄",
            vitality = 100,
            mental = 100,
            strength = 100,
            agility = 100,
            intelligence = 100,
            constitution = 100,
        });
        hero.CombatHpMax = 999;
        hero.CombatHp = 999;

        var run = new QuestRun(definition, startedTurn: 1)
        {
            morale = new MoraleState(999),
        };
        run.formation[0] = hero;

        new QuestProgressor().AdvanceOnePhase(run, currentTurn: 1);

        Assert.True(run.bossDefeated);
        return run;
    }
}

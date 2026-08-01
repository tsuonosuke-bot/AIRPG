using GuildSimulator.Core;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// 遺物システムは <see cref="GameFeatures.RelicsEnabled"/> で凍結している。
/// 「凍結中は完全に効かない」ことと「フラグを戻せばそのまま復活する」ことの両方を守る。
/// </summary>
[Collection("Guild static state")]
public class RelicFreezeTests
{
    [Fact]
    public void RelicsAreFrozenByDefault()
    {
        Assert.False(GameFeatures.RelicsEnabled);
    }

    [Fact]
    public void FrozenRelicsAreNotAcquired()
    {
        var guild = new GuildManager(startGold: 0);

        guild.AddRelic(GoldRelic(), "テスト付与");

        Assert.Empty(guild.relics);
        Assert.DoesNotContain(guild.economyLogs, log => log.Contains("遺物入手"));
    }

    [Fact]
    public void OwnedRelicsStopWorkingWhileFrozenAndComeBackWhenRevived()
    {
        var guild = new GuildManager(startGold: 0);

        // 凍結を解いて入手した状態を作る（＝凍結前のセーブデータ相当）。
        using (new RelicFeatureScope())
        {
            guild.AddRelic(GoldRelic(), "凍結前");
            guild.AddRelic(HpRelic(), "凍結前");
            Assert.Equal(2, guild.relics.Count);
            Assert.Equal(5f, RelicSystem.GetGoldRewardMultiplier());
            RelicSystem.GetUnitModifiers(out var liveAdd, out _);
            Assert.Equal(20, liveAdd.hp);
        }

        // 凍結中は所持したまま効果だけが消える。
        Assert.Equal(2, guild.relics.Count);
        Assert.Equal(1f, RelicSystem.GetGoldRewardMultiplier());
        Assert.Equal(1f, RelicSystem.GetUpkeepMultiplier());
        Assert.Equal(1f, RelicSystem.GetRestHealMultiplier());
        RelicSystem.GetUnitModifiers(out var frozenAdd, out var frozenMul);
        Assert.Equal(0, frozenAdd.hp);
        Assert.Equal(1f, frozenMul.hp);

        // フラグを戻せば、所持記録がそのまま効き始める。
        using (new RelicFeatureScope())
        {
            Assert.Equal(5f, RelicSystem.GetGoldRewardMultiplier());
            RelicSystem.GetUnitModifiers(out var revivedAdd, out _);
            Assert.Equal(20, revivedAdd.hp);
        }
    }

    [Fact]
    public void FrozenRelicsNeverComeOutOfDungeonChests()
    {
        var guild = new GuildManager(startGold: 0);
        var dungeon = new DungeonMasterData { id = "dungeon", dungeonName = "試験場" };
        // 遺物の重みを圧倒的に大きくしておく。抽選に残っていれば必ず遺物ばかりが出る。
        dungeon.treasureTable.Add(new RewardEntryData
        {
            type = RewardType.Relic, relicId = "relic_gold", Relic = GoldRelic(), weight = 100,
        });
        dungeon.treasureTable.Add(new RewardEntryData
        {
            type = RewardType.Gold, gold = 10, weight = 1,
        });

        var run = ChestRun(dungeon, chestCount: 100);
        new QuestRewardService().OpenChests(run, guild, "[完了]");

        // 遺物は1つも出ず、残りの中身（資金）で抽選し直されている。
        Assert.Equal(100, run.pendingLoot.Count);
        Assert.All(run.pendingLoot, e => Assert.Equal(RewardType.Gold, e.type));
    }

    [Fact]
    public void RevivedRelicsComeOutOfDungeonChestsAgain()
    {
        using var relicsEnabled = new RelicFeatureScope();

        var guild = new GuildManager(startGold: 0);
        var dungeon = new DungeonMasterData { id = "dungeon", dungeonName = "試験場" };
        dungeon.treasureTable.Add(new RewardEntryData
        {
            type = RewardType.Relic, relicId = "relic_gold", Relic = GoldRelic(), weight = 100,
        });

        var run = ChestRun(dungeon, chestCount: 20);
        new QuestRewardService().OpenChests(run, guild, "[完了]");

        Assert.All(run.pendingLoot, e => Assert.Equal(RewardType.Relic, e.type));
        Assert.NotEmpty(run.pendingLoot);
    }

    [Fact]
    public void FrozenRelicsNeverComeOutOfBossChests()
    {
        var guild = new GuildManager(startGold: 0);
        var definition = new QuestMasterData
        {
            id = "boss_quest",
            questName = "ボス討伐",
            bossDropsAreGuaranteed = true,
            bossDrops =
            {
                new RewardEntryData
                {
                    type = RewardType.Relic, relicId = "relic_gold", Relic = GoldRelic(), chance = 1f,
                },
                new RewardEntryData { type = RewardType.Gold, gold = 300, chance = 1f },
            },
        };

        var run = new QuestRun(definition, startedTurn: 1);
        run.chests.Add(new TreasureChest { kind = TreasureChestKind.Boss, foundPhase = 1 });
        new QuestRewardService().OpenChests(run, guild, "[完了]");

        // 確定ドロップでも遺物だけは出ない。同じ宝箱の他の中身は今まで通り出る。
        var loot = Assert.Single(run.pendingLoot);
        Assert.Equal(RewardType.Gold, loot.type);
        Assert.Equal(300, loot.gold);
    }

    [Fact]
    public void RelicsLeftInOldSaveLootAreSilentlyDropped()
    {
        var guild = new GuildManager(startGold: 0);
        var definition = new QuestMasterData { id = "quest", questName = "帰還" };
        var run = new QuestRun(definition, startedTurn: 1);
        // 凍結前に積まれたまま持ち越された戦利品を想定する。
        run.pendingLoot.Add(new RewardEntryData
        {
            type = RewardType.Relic, relicId = "relic_gold", Relic = GoldRelic(),
        });
        run.pendingLoot.Add(new RewardEntryData { type = RewardType.Gold, gold = 50 });

        new QuestRewardService().ApplyPendingLoot(run, guild, "[完了]");

        Assert.Empty(guild.relics);
        Assert.Equal(50, guild.Gold);
        Assert.DoesNotContain(run.logs, log => log.Contains("遺物"));
    }

    static RelicMasterData GoldRelic() => new()
    {
        id = "relic_gold",
        relicName = "テストの銀行屋",
        effectType = RelicEffectType.GoldReward_Multiply,
        rate = 5f,
    };

    static RelicMasterData HpRelic() => new()
    {
        id = "relic_hp",
        relicName = "テストの包帯",
        effectType = RelicEffectType.Unit_AddFlat,
        add = new StatBlock { hp = 20 },
    };

    // 未開封の宝箱だけを積んだクエストを作る。空っぽ抽選は合鍵扱いで飛ばし、
    // 「中身の抽選から遺物が外れているか」だけを見る。
    static QuestRun ChestRun(DungeonMasterData dungeon, int chestCount)
    {
        var definition = new QuestMasterData
        {
            id = "treasure_quest",
            questName = "宝探し",
            dungeonId = dungeon.id,
            Dungeon = dungeon,
        };
        var run = new QuestRun(definition, startedTurn: 1)
        {
            guaranteedNonEmptyChestCount = chestCount,
        };
        for (int i = 0; i < chestCount; i++)
            run.chests.Add(new TreasureChest { kind = TreasureChestKind.Dungeon, foundPhase = 1 });
        return run;
    }
}

using GuildSimulator.Core;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using System.Text.Json.Nodes;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Guild static state")]
public class SaveLoadTests
{
    static GameMasterData LoadDb()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        return MasterLoader.Load(dataDir);
    }

    [Fact]
    public void SaveThenLoadRestoresGuildAdventurersAndActiveQuest()
    {
        // 遺物は凍結中でもセーブ形式ごと残す（復活時に所持記録をそのまま使える）。
        // 所持させるには入手経路が要るので、このテストの間だけ有効化する。
        using var relicsEnabled = new RelicFeatureScope();

        var db = LoadDb();
        var guild = new GuildManager(startGold: 200, startRank: 1);
        var questManager = new QuestManager(guild);
        var discoveredEnemies = db.enemies.Values.Take(2).ToList();
        foreach (var enemy in discoveredEnemies)
            guild.DiscoverEnemy(enemy);

        var advMaster = db.allAdventurers.First(a => a.recruitGuildRank <= 1);
        var adv = new AdventurerData(advMaster);
        guild.AddAdventurer(adv);
        adv.AddExperience(150, out _);   // レベルアップさせて経験値・レベルの復元を確認する
        var mastery = adv.OnClearQuest(adv.rank);
        Assert.True(mastery.PointsGained > 0);
        adv.RecordExpedition("過去の調査", "撤退");
        adv.injuries.Add(new AdventurerInjury
        {
            type = InjuryType.Fracture,
            remainingRestTurns = 2,
            scarChancePercent = 35,
        });
        adv.scars.Add(new AdventurerScar { type = ScarType.BattleScar });

        var equip = db.equipment.Values.First();
        guild.AddEquipment(equip, 3, "テスト");

        var relic = db.relics.Values.First();
        guild.AddRelic(relic, "テスト付与");
        var consumable = db.consumables.Values.First();
        guild.AddConsumable(consumable, 2);
        guild.ReplaceShopStock(
            currentTurn: 1,
            new Dictionary<string, int> { [equip.id] = 2 },
            new Dictionary<string, int> { [consumable.id] = 1 });

        var quest = db.allQuests.First(q => !q.isEmergencyQuest);
        var formation = new AdventurerData?[6];
        formation[0] = adv;
        Assert.True(questManager.TryStartQuest(
            quest,
            formation,
            currentTurn: 1,
            out var error,
            policy: ExpeditionPolicy.SurvivalFirst));
        Assert.True(string.IsNullOrEmpty(error));

        var run = questManager.activeQuests.Single();
        run.currentPhase = 2;
        run.retreated = true;
        run.retreatReason = ExpeditionRetreatReason.SurvivalPolicy;
        run.morale.Drain(5);
        run.logs.Add("テストログ");
        run.logs.Add("[Turn 2] Phase 2/10: 旧表記ログ");
        run.AddReportEvent(
            2, 2, ExpeditionEventKind.Discovery, "テスト発見", "Phase 2までの構造化された報告", important: true);
        run.pendingLoot.Add(new RewardEntryData
        {
            type = RewardType.Relic,
            relicId = relic.id,
            Relic = relic,
        });
        run.chests.Add(new TreasureChest { kind = TreasureChestKind.Dungeon, foundPhase = 2 });
        run.chests.Add(new TreasureChest { kind = TreasureChestKind.Boss, foundPhase = 2 });
        run.goldRewardBonusPercent = 25;
        run.restHealBonusPercent = 40;
        run.treasureFromNothingPercent = 25;
        run.enemyFromNothingPercent = 25;
        run.battleExpBonusPercent = 50;
        run.guaranteedNonEmptyChestCount = 1;
        run.emergencyRetreatHpPercent = 25;
        run.bossDefeated = true;
        run.bossFinisherAdventurerId = adv.id;
        run.targetPvBonusByAdventurerId[adv.id] = 1;
        run.targetMpvBonusByAdventurerId[adv.id] = 2;
        run.RecordLevelGrowth(adv.id, new[] { StatType.Vitality, StatType.Agility });
        run.usedConsumableIds.Add(consumable.id);
        var choiceEvent = db.choiceEvents.Values.First();
        run.pendingChoice = new PendingQuestChoice { Event = choiceEvent, createdTurn = 5 };
        adv.RegisterKnockout(severity: 2);

        questManager.FillBoard(db.allQuests, currentTurn: 1);
        var recruitCandidates = new List<AdventurerMasterData> { advMaster };

        string tmpPath = Path.Combine(Path.GetTempPath(), $"airpg_savetest_{Guid.NewGuid():N}.json");
        try
        {
            SaveManager.Save(tmpPath, guild, questManager, currentTurn: 5, recruitCandidates);
            string savedJson = File.ReadAllText(tmpPath);
            Assert.Contains("bossFinisherAdventurerId", savedJson);
            Assert.Contains(adv.id, savedJson);
            var loaded = SaveManager.Load(tmpPath, db);

            Assert.Equal(5, loaded.CurrentTurn);
            Assert.Equal(guild.Gold, loaded.Guild.Gold);
            Assert.Equal(guild.GuildRank, loaded.Guild.GuildRank);
            Assert.Equal(guild.GuildPoints, loaded.Guild.GuildPoints);
            Assert.Equal(guild.economyLogs, loaded.Guild.economyLogs);
            Assert.Equal(
                discoveredEnemies.Select(enemy => enemy.id).OrderBy(id => id),
                loaded.Guild.DiscoveredEnemyIds.OrderBy(id => id));

            var loadedAdv = Assert.Single(loaded.Guild.adventurers);
            Assert.Equal(adv.id, loadedAdv.id);
            Assert.Equal(adv.name, loadedAdv.name);
            Assert.Equal(adv.level, loadedAdv.level);
            Assert.Equal(adv.experience, loadedAdv.experience);
            Assert.Equal(adv.vitality, loadedAdv.vitality);
            Assert.Equal(adv.CurrentClassMastery, loadedAdv.CurrentClassMastery);
            Assert.Equal(adv.expeditionCount, loadedAdv.expeditionCount);
            Assert.Equal(adv.retreatCount, loadedAdv.retreatCount);
            Assert.Equal(adv.adventureHistory, loadedAdv.adventureHistory);
            Assert.True(loadedAdv.isIncapacitated);
            Assert.Equal(2, loadedAdv.pendingInjurySeverity);
            Assert.Equal(InjuryType.Fracture, Assert.Single(loadedAdv.injuries).type);
            Assert.Equal(ScarType.BattleScar, Assert.Single(loadedAdv.scars).type);

            Assert.Equal(guild.GetCount(equip), loaded.Guild.GetCount(equip));
            Assert.Equal(2, loaded.Guild.GetConsumableCount(consumable));
            Assert.Equal(1, loaded.Guild.LastShopRefreshTurn);
            Assert.Equal(2, loaded.Guild.shopEquipmentStock[equip.id]);
            Assert.Single(loaded.Guild.relics);
            Assert.Same(relic, loaded.Guild.relics[0]);   // マスタ参照はDBの同一インスタンスに解決される

            var loadedRun = Assert.Single(loaded.QuestManager.activeQuests);
            Assert.Same(quest, loadedRun.def);
            Assert.Equal(2, loadedRun.currentPhase);
            Assert.Equal(run.morale.Current, loadedRun.morale.Current);
            Assert.Equal(run.morale.Max, loadedRun.morale.Max);
            Assert.Contains("テストログ", loadedRun.logs);
            Assert.Contains("[Turn 2] エリア 2/10: 旧表記ログ", loadedRun.logs);
            Assert.DoesNotContain(loadedRun.logs, log => log.Contains("Phase") || log.Contains("フェーズ"));
            Assert.Same(loadedAdv, loadedRun.formation[0]);   // 編成の参照は復元済みadventurerと一致する
            Assert.Equal(run.startingLevels, loadedRun.startingLevels);
            Assert.Equal(
                run.levelGrowthsByAdventurerId[adv.id],
                loadedRun.levelGrowthsByAdventurerId[adv.id]);
            Assert.Equal(run.guildUpkeepAtStart, loadedRun.guildUpkeepAtStart);
            Assert.Equal(ExpeditionPolicy.SurvivalFirst, loadedRun.policy);
            Assert.True(loadedRun.retreated);
            Assert.Equal(ExpeditionRetreatReason.SurvivalPolicy, loadedRun.retreatReason);
            var loadedReport = Assert.Single(loadedRun.reportEvents, e => e.title == "テスト発見");
            Assert.Equal("エリア 2までの構造化された報告", loadedReport.detail);

            var loadedLoot = Assert.Single(loadedRun.pendingLoot);
            Assert.Same(relic, loadedLoot.Relic);
            Assert.Equal(2, loadedRun.chests.Count);
            Assert.Single(loadedRun.chests, chest => chest.IsBossChest);
            Assert.All(loadedRun.chests, chest => Assert.Equal(2, chest.foundPhase));
            Assert.Equal(25, loadedRun.goldRewardBonusPercent);
            Assert.Equal(40, loadedRun.restHealBonusPercent);
            Assert.Equal(25, loadedRun.treasureFromNothingPercent);
            Assert.Equal(25, loadedRun.enemyFromNothingPercent);
            Assert.Equal(50, loadedRun.battleExpBonusPercent);
            Assert.Equal(1, loadedRun.guaranteedNonEmptyChestCount);
            Assert.Equal(25, loadedRun.emergencyRetreatHpPercent);
            Assert.True(loadedRun.bossDefeated);
            Assert.Equal(adv.id, loadedRun.bossFinisherAdventurerId);
            Assert.Equal(1, loadedRun.targetPvBonusByAdventurerId[adv.id]);
            Assert.Equal(2, loadedRun.targetMpvBonusByAdventurerId[adv.id]);
            Assert.Contains(consumable.id, loadedRun.usedConsumableIds);
            Assert.Equal(choiceEvent.id, loadedRun.pendingChoice?.Event.id);

            Assert.Equal(questManager.questBoard.Select(e => e.quest.id), loaded.QuestManager.questBoard.Select(e => e.quest.id));
            Assert.Equal(recruitCandidates.Select(a => a.id), loaded.RecruitCandidates.Select(a => a.id));
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    [Fact]
    public void CompletedQuestHistorySurvivesWithoutCurrentMasterOrAdventurerReferences()
    {
        var db = LoadDb();
        var guild = new GuildManager(startGold: 200, startRank: 1);
        var questManager = new QuestManager(guild);
        var completedRun = new QuestRun(new QuestMasterData
        {
            id = "removed_quest_id",
            questName = "削除済みマスタの依頼名",
            totalPhases = 3,
        }, startedTurn: 7)
        {
            currentPhase = 3,
            retreated = true,
        };
        completedRun.logs.AddRange(new[]
        {
            "[Turn 8] Phase 3: 戦闘開始 冒険者 vs 旧敵 Lv1 ×1",
            "  Phase 3: 埋葬済み冒険者 RETIRED_MEMBER_BATTLE_LOG",
            "[Turn 8] Phase 3/3: 敵遭遇：旧敵 - 撤退（HP 1/10 士気 0/8）",
            "[Turn 8] Phase 3/3: RETIRED_MEMBER_EXPEDITION_LOG",
        });
        completedRun.AddReportEvent(
            8, 3, ExpeditionEventKind.Retreat, "撤退", "埋葬済み冒険者が帰還した", important: true);
        questManager.activeQuests.Add(completedRun);
        questManager.FinalizeQuest(completedRun);

        string json = SaveManager.Serialize(
            guild,
            questManager,
            currentTurn: 9,
            new List<AdventurerMasterData>());
        var loaded = SaveManager.Deserialize(json, db);

        Assert.Empty(loaded.Guild.adventurers);
        var restored = Assert.Single(loaded.QuestManager.QuestHistory);
        Assert.Equal("removed_quest_id", restored.QuestId);
        Assert.Equal("削除済みマスタの依頼名", restored.QuestName);
        Assert.Equal(QuestHistoryOutcome.Retreat, restored.Outcome);
        Assert.DoesNotContain(restored.Logs, log => log.Contains("Phase"));
        var index = QuestLogIndexer.Build(restored.Logs);
        Assert.Single(index.Battles);
        Assert.Contains(index.Battles[0].Lines, line => line.Contains("RETIRED_MEMBER_BATTLE_LOG"));
        Assert.Contains(index.ExpeditionLogs, line => line.Contains("RETIRED_MEMBER_EXPEDITION_LOG"));
    }

    [Fact]
    public void OversizedQuestHistoryIsBoundedAgainWhenLoaded()
    {
        var db = LoadDb();
        var guild = new GuildManager(startGold: 200, startRank: 1);
        var questManager = new QuestManager(guild);
        questManager.RestoreState(
            new List<QuestBoardEntry>(),
            new List<QuestRun>(),
            Array.Empty<string>(),
            questHistoryToRestore: new[]
            {
                new QuestHistoryEntry(
                    "oversized_saved_history",
                    "長大な保存履歴",
                    1,
                    2,
                    QuestHistoryOutcome.Success,
                    new[] { "保存前は短いログ" }),
            });

        string json = SaveManager.Serialize(
            guild,
            questManager,
            currentTurn: 3,
            new List<AdventurerMasterData>());
        var root = JsonNode.Parse(json)!.AsObject();
        var savedLogs = root["questManager"]!["questHistory"]![0]!["logs"]!.AsArray();
        savedLogs.Clear();
        for (int index = 0; index < QuestHistoryEntry.MaxLogLines + 50; index++)
            savedLogs.Add($"LOAD_{index:D4}_{new string('x', 100)}");
        savedLogs.Add("LOAD_NEWEST_MUST_REMAIN");

        var loaded = SaveManager.Deserialize(root.ToJsonString(), db);

        var restored = Assert.Single(loaded.QuestManager.QuestHistory);
        Assert.InRange(restored.Logs.Count, 1, QuestHistoryEntry.MaxLogLines);
        Assert.InRange(restored.LogCharacterCount, 1, QuestHistoryEntry.MaxLogCharacters);
        Assert.Equal(QuestHistoryEntry.OmissionMarker, restored.Logs[0]);
        Assert.Equal("LOAD_NEWEST_MUST_REMAIN", restored.Logs[^1]);
        Assert.DoesNotContain(restored.Logs, line => line.Contains("LOAD_0000_"));
    }

    [Fact]
    public void MaximumJapaneseQuestHistoryStaysWithinWebStorageJsonBudget()
    {
        const int safeJsonCharacterBudget = 2_500_000;
        var db = LoadDb();
        var guild = new GuildManager(startGold: 200, startRank: 1);
        var questManager = new QuestManager(guild);
        var histories = Enumerable.Range(0, QuestManager.QuestHistoryLimit)
            .Select(index => new QuestHistoryEntry(
                $"max_japanese_{index}",
                $"日本語中心の最大履歴{index}",
                index + 1,
                index + 2,
                QuestHistoryOutcome.Success,
                new[] { new string('記', QuestHistoryEntry.MaxLogCharacters * 2) }))
            .ToList();
        questManager.RestoreState(
            new List<QuestBoardEntry>(),
            new List<QuestRun>(),
            Array.Empty<string>(),
            questHistoryToRestore: histories);

        string json = SaveManager.Serialize(
            guild,
            questManager,
            currentTurn: 99,
            new List<AdventurerMasterData>());

        Assert.Equal(QuestManager.QuestHistoryLimit, questManager.QuestHistory.Count);
        Assert.All(
            questManager.QuestHistory,
            history => Assert.Equal(QuestHistoryEntry.MaxLogCharacters, history.LogCharacterCount));
        Assert.Contains(new string('記', 32), json);
        Assert.DoesNotContain("\\u8A18", json);
        Assert.True(
            json.Length < safeJsonCharacterBudget,
            $"最大履歴のセーブJSONが安全閾値を超えました: {json.Length:N0}文字");
        Assert.Equal(
            QuestManager.QuestHistoryLimit,
            SaveManager.Deserialize(json, db).QuestManager.QuestHistory.Count);
    }

    [Fact]
    public void FinalizedFatalityHistorySurvivesBurialAndSaveLoad()
    {
        using var random = GameRandom.UseSeed(1);
        var db = LoadDb();
        var guild = new GuildManager(startGold: 500, startRank: 1);
        var questManager = new QuestManager(guild);
        var adventurer = new AdventurerData(
            db.allAdventurers.First(candidate => candidate.recruitGuildRank <= 1));
        guild.AddAdventurer(adventurer);
        var run = new QuestRun(new QuestMasterData
        {
            id = "fatal_history_quest",
            questName = "戦没記録の依頼",
            totalPhases = 3,
        }, startedTurn: 7)
        {
            currentPhase = 3,
            failed = true,
        };
        run.formation[0] = adventurer;
        run.logs.AddRange(new[]
        {
            "[Turn 8] エリア 3: 戦闘開始 冒険者 vs 旧敵 Lv1 ×1",
            $"  エリア 3: {adventurer.name} FATALITY_MEMBER_BATTLE_LOG",
            "[Turn 8] エリア 3/3: 敵遭遇：旧敵 - 全員戦闘不能",
        });
        adventurer.RegisterKnockout(severity: 3);
        questManager.activeQuests.Add(run);

        questManager.FinalizeQuest(run);

        Assert.False(adventurer.isAlive);
        var finalized = Assert.Single(questManager.QuestHistory);
        Assert.Contains(finalized.Logs, line => line.Contains(adventurer.name));
        Assert.Contains(finalized.Logs, line => line.Contains("FATALITY_MEMBER_BATTLE_LOG"));
        Assert.Contains(finalized.Logs, line => line.Contains("死亡した"));
        Assert.True(guild.TryBuryAdventurer(adventurer, currentTurn: 9, out string reason), reason);
        Assert.Empty(guild.adventurers);

        string json = SaveManager.Serialize(
            guild,
            questManager,
            currentTurn: 9,
            new List<AdventurerMasterData>());
        var loaded = SaveManager.Deserialize(json, db);

        Assert.Empty(loaded.Guild.adventurers);
        Assert.Equal(adventurer.name, Assert.Single(loaded.Guild.burialRecords).name);
        var restored = Assert.Single(loaded.QuestManager.QuestHistory);
        Assert.Contains(restored.Logs, line => line.Contains(adventurer.name));
        Assert.Contains(restored.Logs, line => line.Contains("死亡した"));
        var index = QuestLogIndexer.Build(restored.Logs);
        var battle = Assert.Single(index.Battles);
        Assert.Contains(battle.Lines, line => line.Contains("FATALITY_MEMBER_BATTLE_LOG"));
    }

    [Fact]
    public void VersionTenSaveWithoutQuestHistoryLoadsAsEmptyHistory()
    {
        var db = LoadDb();
        var guild = new GuildManager(startGold: 200, startRank: 1);
        var questManager = new QuestManager(guild);
        string json = SaveManager.Serialize(
            guild,
            questManager,
            currentTurn: 1,
            new List<AdventurerMasterData>());
        var root = JsonNode.Parse(json)!.AsObject();
        root["saveVersion"] = 10;
        Assert.True(root["questManager"]!.AsObject().Remove("questHistory"));

        var loaded = SaveManager.Deserialize(root.ToJsonString(), db);

        Assert.Empty(loaded.QuestManager.QuestHistory);
    }

    [Fact]
    public void LegacyRenownChoiceDoesNotGrantASecondBossTraitOffer()
    {
        var db = LoadDb();
        var guild = new GuildManager(startGold: 200, startRank: 1);
        var questManager = new QuestManager(guild);
        var adventurer = new AdventurerData(
            db.allAdventurers.First(candidate => candidate.recruitGuildRank <= 1));
        adventurer.offeredTraitIds.Add("trait_renown");
        adventurer.records.Add(ExpeditionRecordType.BossKills, 3);
        guild.AddAdventurer(adventurer);

        string legacyJson = SaveManager.Serialize(
                guild,
                questManager,
                currentTurn: 5,
                new List<AdventurerMasterData>())
            .Replace(
                $"\"saveVersion\": {SaveGameData.CurrentVersion}",
                "\"saveVersion\": 9",
                StringComparison.Ordinal);

        var loaded = SaveManager.Deserialize(legacyJson, db);
        var loadedAdventurer = Assert.Single(loaded.Guild.adventurers);

        Assert.Equal(3, loadedAdventurer.records[ExpeditionRecordType.BossKills]);
        Assert.Contains("trait_renown", loadedAdventurer.offeredTraitIds);
        Assert.Contains("trait_boss_footwork", loadedAdventurer.offeredTraitIds);
        Assert.Contains("trait_trophy_eye", loadedAdventurer.offeredTraitIds);
        Assert.Empty(TraitSystem.BuildOffers(
            new[] { loadedAdventurer },
            db.traits.Values));
    }
}

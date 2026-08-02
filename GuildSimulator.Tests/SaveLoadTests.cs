using GuildSimulator.Game.Data;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
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
        run.targetPvBonusByAdventurerId[adv.id] = 1;
        run.targetMpvBonusByAdventurerId[adv.id] = 2;
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
}

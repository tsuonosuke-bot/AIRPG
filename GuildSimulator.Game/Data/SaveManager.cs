using System.Text.Json;
using System.Text.Json.Serialization;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Quest;

namespace GuildSimulator.Game.Data;

public record LoadedGame(
    GuildManager Guild,
    QuestManager QuestManager,
    int CurrentTurn,
    List<AdventurerMasterData> RecruitCandidates);

public static class SaveManager
{
    static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultSavePath
    {
        get
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Saves");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); }
                catch (IOException) { dir = Directory.GetCurrentDirectory(); }
                catch (UnauthorizedAccessException) { dir = Directory.GetCurrentDirectory(); }
            }
            return Path.Combine(dir, "save1.json");
        }
    }

    public static bool Exists(string path) => File.Exists(path);

    // ---- Save ----

    /// <summary>
    /// セーブ内容をJSON文字列にする。保存先を持たないホスト（ブラウザのlocalStorage等）は
    /// この結果を自前で永続化する。
    /// </summary>
    public static string Serialize(
        GuildManager guild,
        QuestManager questManager,
        int currentTurn,
        List<AdventurerMasterData> recruitCandidates)
    {
        var data = new SaveGameData
        {
            currentTurn = currentTurn,
            guild = ExportGuild(guild),
            questManager = ExportQuestManager(questManager),
            recruitCandidateIds = recruitCandidates.Select(a => a.id).ToList(),
        };
        return JsonSerializer.Serialize(data, _opts);
    }

    public static void Save(
        string path,
        GuildManager guild,
        QuestManager questManager,
        int currentTurn,
        List<AdventurerMasterData> recruitCandidates)
    {
        string json = Serialize(guild, questManager, currentTurn, recruitCandidates);

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        File.WriteAllText(path, json);
    }

    static GuildSaveData ExportGuild(GuildManager guild) => new()
    {
        gold = guild.Gold,
        guildRank = guild.GuildRank,
        guildPoints = guild.GuildPoints,
        economyLogs = new List<string>(guild.economyLogs),
        relicIds = guild.relics.Select(r => r.id).ToList(),
        facilityIds = guild.facilities.Select(f => f.id).ToList(),
        inventory = guild.GetInventoryView()
            .Select(s => new InventoryEntrySave { itemId = s.item.id, count = s.count })
            .ToList(),
        consumables = guild.GetConsumablesView()
            .Select(s => new InventoryEntrySave { itemId = s.item.id, count = s.count })
            .ToList(),
        lastShopRefreshTurn = guild.LastShopRefreshTurn,
        shopEquipmentStock = new Dictionary<string, int>(guild.shopEquipmentStock),
        shopConsumableStock = new Dictionary<string, int>(guild.shopConsumableStock),
        adventurers = guild.adventurers.Select(ExportAdventurer).ToList(),
        burialRecords = guild.burialRecords.Select(b => new BurialRecordSave
        {
            name = b.name,
            level = b.level,
            classAndRace = b.classAndRace,
            buriedTurn = b.buriedTurn,
            expeditionCount = b.expeditionCount,
            successCount = b.successCount,
        }).ToList(),
    };

    static AdventurerSaveData ExportAdventurer(AdventurerData a) => new()
    {
        id = a.id,
        masterId = a.master.id,
        name = a.name,
        level = a.level,
        experience = a.experience,
        isAlive = a.isAlive,
        rank = a.rank,
        rankPoint = a.rankPoint,
        raceId = a.race?.id ?? "",
        classId = a.currentClass?.id ?? "",
        weaponId = a.Weapon?.id ?? "",
        armorId = a.Armor?.id ?? "",
        equippedSlotIds = a.GetAllEquipped().ToDictionary(kv => kv.Key, kv => kv.Value.id),
        vitality = a.vitality,
        mental = a.mental,
        strength = a.strength,
        agility = a.agility,
        intelligence = a.intelligence,
        constitution = a.constitution,
        appearance = a.appearance,
        combatHp = a.CombatHp,
        combatHpMax = a.CombatHpMax,
        expeditionCount = a.expeditionCount,
        successfulExpeditionCount = a.successfulExpeditionCount,
        retreatCount = a.retreatCount,
        adventureHistory = new List<string>(a.adventureHistory),
        learnedSkills = a.ExportLearnedSkills()
            .Select(x => new LearnedSkillSave { skillId = x.skill.id, ownerClassId = x.ownerClass?.id })
            .ToList(),
        classClearCounts = new Dictionary<string, int>(a.ExportClassClearCounts()),
    };

    static QuestManagerSaveData ExportQuestManager(QuestManager qm) => new()
    {
        questBoard = qm.questBoard
            .Select(e => new BoardEntrySave { questId = e.quest.id, postedTurn = e.postedTurn })
            .ToList(),
        activeQuests = qm.activeQuests.Select(ExportQuestRun).ToList(),
        clearedOneShotIds = qm.ExportClearedOneShotIds().ToList(),
        clearedQuestIds = qm.ExportClearedQuestIds().ToList(),
        discoveredClueIds = qm.ExportDiscoveredClueIds().ToList(),
        selectedBranchIds = qm.ExportSelectedBranchIds().ToList(),
    };

    static QuestRunSaveData ExportQuestRun(QuestRun q) => new()
    {
        questId = q.def.id,
        startedTurn = q.startedTurn,
        currentPhase = q.currentPhase,
        failed = q.failed,
        retreated = q.retreated,
        retreatReason = q.retreatReason,
        moraleCurrent = q.morale.Current,
        moraleMax = q.morale.Max,
        rewarded = q.rewarded,
        completed = q.completed,
        bossDefeated = q.bossDefeated,
        baseRewardsApplied = q.baseRewardsApplied,
        clearProgressApplied = q.clearProgressApplied,
        formationAdventurerIds = q.formation.Select(a => a?.id).ToArray(),
        logs = new List<string>(q.logs),
        reportEvents = q.reportEvents.Select(e => new ExpeditionEventSave
        {
            turn = e.turn,
            phase = e.phase,
            kind = e.kind,
            title = e.title,
            detail = e.detail,
            actorName = e.actorName,
            clueId = e.clueId,
            important = e.important,
        }).ToList(),
        discoveredClueIds = new List<string>(q.discoveredClueIds),
        policy = q.policy,
        startingLevels = new Dictionary<string, int>(q.startingLevels),
        guildUpkeepAtStart = q.guildUpkeepAtStart,
        pendingLoot = q.pendingLoot.Select(e => new PendingLootSave
        {
            type = e.type,
            relicId = e.relicId,
            equipmentId = e.equipmentId,
            skillId = e.skillId,
            consumableId = e.consumableId,
            gold = e.gold,
            weight = e.weight,
            quantity = e.quantity,
            unique = e.unique,
        }).ToList(),
        chests = q.chests
            .Select(c => new TreasureChestSave { kind = c.kind, foundPhase = c.foundPhase })
            .ToList(),
        gatheredCount = q.gatheredCount,
        usedConsumableIds = new List<string>(q.usedConsumableIds),
        goldRewardBonusPercent = q.goldRewardBonusPercent,
        expRewardBonusPercent = q.expRewardBonusPercent,
        trapDamageReductionPercent = q.trapDamageReductionPercent,
        pendingChoiceEventId = q.pendingChoice?.Event.id ?? "",
        pendingChoiceCreatedTurn = q.pendingChoice?.createdTurn ?? 0,
    };

    // ---- Load ----

    public static LoadedGame Load(string path, GameMasterData db) =>
        Deserialize(File.ReadAllText(path), db);

    /// <summary><see cref="Serialize"/> が作ったJSONからゲーム状態を復元する。</summary>
    public static LoadedGame Deserialize(string json, GameMasterData db)
    {
        var data = JsonSerializer.Deserialize<SaveGameData>(json, _opts)
            ?? throw new InvalidDataException("セーブデータの読み込みに失敗しました");

        var questById = db.allQuests.ToDictionary(q => q.id);
        var adventurerMasterById = db.allAdventurers.ToDictionary(a => a.id);

        var guild = new GuildManager(startGold: data.guild.gold, startRank: data.guild.guildRank);
        guild.RestoreEconomy(data.guild.gold, data.guild.guildRank, data.guild.guildPoints);
        guild.economyLogs.Clear();
        guild.economyLogs.AddRange(data.guild.economyLogs);

        guild.relics.Clear();
        foreach (var relicId in data.guild.relicIds)
            if (db.relics.TryGetValue(relicId, out var relic))
                guild.relics.Add(relic);
        RelicSystem.SetRelics(guild.relics);

        var facilities = data.guild.facilityIds
            .Where(db.facilities.ContainsKey)
            .Select(id => db.facilities[id])
            .ToList();
        guild.RestoreFacilities(facilities);

        foreach (var entry in data.guild.inventory)
            if (db.equipment.TryGetValue(entry.itemId, out var item))
                guild.AddEquipment(item, entry.count);
        foreach (var entry in data.guild.consumables)
            if (db.consumables.TryGetValue(entry.itemId, out var consumable))
                guild.AddConsumable(consumable, entry.count);
        guild.RestoreShopStock(
            data.guild.lastShopRefreshTurn,
            new Dictionary<string, int>(data.guild.shopEquipmentStock),
            new Dictionary<string, int>(data.guild.shopConsumableStock));

        var adventurersById = new Dictionary<string, AdventurerData>();
        guild.adventurers.Clear();
        foreach (var savedAdv in data.guild.adventurers)
        {
            if (!adventurerMasterById.TryGetValue(savedAdv.masterId, out var master)) continue;
            var adv = RestoreAdventurer(savedAdv, master, db);
            guild.adventurers.Add(adv);
            adventurersById[adv.id] = adv;
        }

        if (data.guild.burialRecords.Count > 0)
            guild.RestoreBurialRecords(data.guild.burialRecords.Select(b =>
                new BurialRecord(b.name, b.level, b.classAndRace, b.buriedTurn, b.expeditionCount, b.successCount)));

        var questManager = new QuestManager(guild);
        var board = data.questManager.questBoard
            .Where(e => questById.ContainsKey(e.questId))
            .Select(e => new QuestBoardEntry(questById[e.questId], e.postedTurn))
            .ToList();
        var active = data.questManager.activeQuests
            .Where(q => questById.ContainsKey(q.questId))
            .Select(q => RestoreQuestRun(q, questById[q.questId], adventurersById, db))
            .ToList();
        questManager.RestoreState(
            board,
            active,
            data.questManager.clearedOneShotIds ?? new(),
            data.questManager.clearedQuestIds ?? new(),
            data.questManager.discoveredClueIds ?? new(),
            data.questManager.selectedBranchIds ?? new());

        var recruitCandidates = data.recruitCandidateIds
            .Where(adventurerMasterById.ContainsKey)
            .Select(id => adventurerMasterById[id])
            .ToList();

        return new LoadedGame(guild, questManager, data.currentTurn, recruitCandidates);
    }

    static AdventurerData RestoreAdventurer(AdventurerSaveData saved, AdventurerMasterData master, GameMasterData db)
    {
        var adv = new AdventurerData(master)
        {
            id = saved.id,
            name = saved.name,
            level = saved.level,
            experience = saved.experience,
            isAlive = saved.isAlive,
            // 冒険者ランクに上限がなかった頃のセーブは7(S)を超えていることがある。
            rank = Rank.Clamp(saved.rank),
            rankPoint = saved.rankPoint,
            race = db.races.GetValueOrDefault(saved.raceId),
            currentClass = db.classes.GetValueOrDefault(saved.classId),
            vitality = saved.vitality,
            mental = saved.mental,
            strength = saved.strength,
            agility = saved.agility,
            intelligence = saved.intelligence,
            constitution = saved.constitution,
            appearance = saved.appearance,
            CombatHp = saved.combatHp,
            CombatHpMax = saved.combatHpMax,
            expeditionCount = saved.expeditionCount,
            successfulExpeditionCount = saved.successfulExpeditionCount,
            retreatCount = saved.retreatCount,
            adventureHistory = new List<string>(saved.adventureHistory ?? new()),
        };

        // スロットベース装備の復元（v4以降）。無ければ旧形式からマイグレーション。
        foreach (var slot in EquipService.AllSlots)
            adv.SetEquipped(slot, null);

        if (saved.equippedSlotIds != null && saved.equippedSlotIds.Count > 0)
        {
            foreach (var (slot, itemId) in saved.equippedSlotIds)
                if (!string.IsNullOrEmpty(itemId) && db.equipment.TryGetValue(itemId, out var item))
                    adv.SetEquipped(slot, item);
        }
        else
        {
            if (!string.IsNullOrEmpty(saved.weaponId) && db.equipment.TryGetValue(saved.weaponId, out var weapon))
                adv.SetEquipped(EquipSlot.RightHand, weapon);
            if (!string.IsNullOrEmpty(saved.armorId) && db.equipment.TryGetValue(saved.armorId, out var armor))
                adv.SetEquipped(EquipSlot.Body, armor);
        }

        var skills = saved.learnedSkills
            .Where(ls => db.skills.ContainsKey(ls.skillId))
            .Select(ls => (
                skill: db.skills[ls.skillId],
                ownerClass: ls.ownerClassId != null ? db.classes.GetValueOrDefault(ls.ownerClassId) : null))
            .ToList();
        adv.RestoreProgress(skills, saved.classClearCounts);

        return adv;
    }

    static QuestRun RestoreQuestRun(
        QuestRunSaveData saved,
        QuestMasterData def,
        Dictionary<string, AdventurerData> adventurersById,
        GameMasterData db)
    {
        var run = new QuestRun(def, saved.startedTurn)
        {
            currentPhase = saved.currentPhase,
            failed = saved.failed,
            retreated = saved.retreated,
            retreatReason = saved.retreatReason,
            morale = new MoraleState(saved.moraleMax, saved.moraleCurrent),
            rewarded = saved.rewarded,
            completed = saved.completed,
            bossDefeated = saved.bossDefeated,
            baseRewardsApplied = saved.baseRewardsApplied,
            clearProgressApplied = saved.clearProgressApplied,
            policy = saved.policy,
            startingLevels = new Dictionary<string, int>(saved.startingLevels),
            guildUpkeepAtStart = saved.guildUpkeepAtStart,
            gatheredCount = saved.gatheredCount,
            goldRewardBonusPercent = saved.goldRewardBonusPercent,
            expRewardBonusPercent = saved.expRewardBonusPercent,
            trapDamageReductionPercent = saved.trapDamageReductionPercent,
        };
        run.logs.AddRange(saved.logs);
        foreach (var e in saved.reportEvents ?? new())
            run.reportEvents.Add(new ExpeditionEventRecord
            {
                turn = e.turn,
                phase = e.phase,
                kind = e.kind,
                title = e.title,
                detail = e.detail,
                actorName = e.actorName,
                clueId = e.clueId,
                important = e.important,
            });
        run.discoveredClueIds.AddRange(saved.discoveredClueIds ?? new());
        run.usedConsumableIds.AddRange(saved.usedConsumableIds);
        if (!string.IsNullOrEmpty(saved.pendingChoiceEventId)
            && db.choiceEvents.TryGetValue(saved.pendingChoiceEventId, out var pendingEvent))
            run.pendingChoice = new PendingQuestChoice
            {
                Event = pendingEvent,
                createdTurn = saved.pendingChoiceCreatedTurn,
            };

        foreach (var loot in saved.pendingLoot)
        {
            run.pendingLoot.Add(new RewardEntryData
            {
                type = loot.type,
                relicId = loot.relicId,
                equipmentId = loot.equipmentId,
                skillId = loot.skillId,
                consumableId = loot.consumableId,
                gold = loot.gold,
                weight = loot.weight,
                quantity = Math.Max(1, loot.quantity),
                unique = loot.unique,
                Relic = db.relics.GetValueOrDefault(loot.relicId),
                Equipment = db.equipment.GetValueOrDefault(loot.equipmentId),
                Skill = db.skills.GetValueOrDefault(loot.skillId),
                Consumable = db.consumables.GetValueOrDefault(loot.consumableId),
            });
        }

        foreach (var chest in saved.chests)
            run.chests.Add(new TreasureChest { kind = chest.kind, foundPhase = chest.foundPhase });

        for (int i = 0; i < run.formation.Length && i < saved.formationAdventurerIds.Length; i++)
        {
            var id = saved.formationAdventurerIds[i];
            if (id != null && adventurersById.TryGetValue(id, out var adv))
                run.formation[i] = adv;
        }

        return run;
    }
}

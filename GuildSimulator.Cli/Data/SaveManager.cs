using System.Text.Json;
using System.Text.Json.Serialization;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;

namespace GuildSimulator.Cli.Data;

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

    public static void Save(
        string path,
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

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(data, _opts);
        File.WriteAllText(path, json);
    }

    static GuildSaveData ExportGuild(GuildManager guild) => new()
    {
        gold = guild.Gold,
        guildRank = guild.GuildRank,
        guildPoints = guild.GuildPoints,
        economyLogs = new List<string>(guild.economyLogs),
        relicIds = guild.relics.Select(r => r.id).ToList(),
        inventory = guild.GetInventoryView()
            .Select(s => new InventoryEntrySave { itemId = s.item.id, count = s.count })
            .ToList(),
        adventurers = guild.adventurers.Select(ExportAdventurer).ToList(),
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
        weaponId = a.weapon?.id ?? "",
        armorId = a.armor?.id ?? "",
        vitality = a.vitality,
        mental = a.mental,
        strength = a.strength,
        agility = a.agility,
        intelligence = a.intelligence,
        constitution = a.constitution,
        appearance = a.appearance,
        combatHp = a.CombatHp,
        combatHpMax = a.CombatHpMax,
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
    };

    static QuestRunSaveData ExportQuestRun(QuestRun q) => new()
    {
        questId = q.def.id,
        startedTurn = q.startedTurn,
        currentPhase = q.currentPhase,
        failed = q.failed,
        retreated = q.retreated,
        moraleCurrent = q.morale.Current,
        moraleMax = q.morale.Max,
        rewarded = q.rewarded,
        completed = q.completed,
        rewardPresented = q.rewardPresented,
        bossDefeated = q.bossDefeated,
        baseRewardsApplied = q.baseRewardsApplied,
        extraRewardTaken = q.extraRewardTaken,
        clearProgressApplied = q.clearProgressApplied,
        formationAdventurerIds = q.formation.Select(a => a?.id).ToArray(),
        logs = new List<string>(q.logs),
        pendingLoot = q.pendingLoot.Select(e => new PendingLootSave
        {
            type = e.type,
            relicId = e.relicId,
            equipmentId = e.equipmentId,
            skillId = e.skillId,
            gold = e.gold,
            weight = e.weight,
            unique = e.unique,
        }).ToList(),
        gatheredCount = q.gatheredCount,
    };

    // ---- Load ----

    public static LoadedGame Load(string path, GameMasterData db)
    {
        string json = File.ReadAllText(path);
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

        foreach (var entry in data.guild.inventory)
            if (db.equipment.TryGetValue(entry.itemId, out var item))
                guild.AddEquipment(item, entry.count);

        var adventurersById = new Dictionary<string, AdventurerData>();
        guild.adventurers.Clear();
        foreach (var savedAdv in data.guild.adventurers)
        {
            if (!adventurerMasterById.TryGetValue(savedAdv.masterId, out var master)) continue;
            var adv = RestoreAdventurer(savedAdv, master, db);
            guild.adventurers.Add(adv);
            adventurersById[adv.id] = adv;
        }

        var questManager = new QuestManager(guild);
        var board = data.questManager.questBoard
            .Where(e => questById.ContainsKey(e.questId))
            .Select(e => new QuestBoardEntry(questById[e.questId], e.postedTurn))
            .ToList();
        var active = data.questManager.activeQuests
            .Where(q => questById.ContainsKey(q.questId))
            .Select(q => RestoreQuestRun(q, questById[q.questId], adventurersById, db))
            .ToList();
        questManager.RestoreState(board, active, data.questManager.clearedOneShotIds);

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
            rank = saved.rank,
            rankPoint = saved.rankPoint,
            race = db.races.GetValueOrDefault(saved.raceId),
            currentClass = db.classes.GetValueOrDefault(saved.classId),
            weapon = db.equipment.GetValueOrDefault(saved.weaponId),
            armor = db.equipment.GetValueOrDefault(saved.armorId),
            vitality = saved.vitality,
            mental = saved.mental,
            strength = saved.strength,
            agility = saved.agility,
            intelligence = saved.intelligence,
            constitution = saved.constitution,
            appearance = saved.appearance,
            CombatHp = saved.combatHp,
            CombatHpMax = saved.combatHpMax,
        };

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
            morale = new MoraleState(saved.moraleMax, saved.moraleCurrent),
            rewarded = saved.rewarded,
            completed = saved.completed,
            rewardPresented = saved.rewardPresented,
            bossDefeated = saved.bossDefeated,
            baseRewardsApplied = saved.baseRewardsApplied,
            extraRewardTaken = saved.extraRewardTaken,
            clearProgressApplied = saved.clearProgressApplied,
            gatheredCount = saved.gatheredCount,
        };
        run.logs.AddRange(saved.logs);

        foreach (var loot in saved.pendingLoot)
        {
            run.pendingLoot.Add(new RewardEntryData
            {
                type = loot.type,
                relicId = loot.relicId,
                equipmentId = loot.equipmentId,
                skillId = loot.skillId,
                gold = loot.gold,
                weight = loot.weight,
                unique = loot.unique,
                Relic = db.relics.GetValueOrDefault(loot.relicId),
                Equipment = db.equipment.GetValueOrDefault(loot.equipmentId),
                Skill = db.skills.GetValueOrDefault(loot.skillId),
            });
        }

        for (int i = 0; i < run.formation.Length && i < saved.formationAdventurerIds.Length; i++)
        {
            var id = saved.formationAdventurerIds[i];
            if (id != null && adventurersById.TryGetValue(id, out var adv))
                run.formation[i] = adv;
        }

        return run;
    }
}

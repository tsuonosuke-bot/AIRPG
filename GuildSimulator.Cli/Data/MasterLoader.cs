using System.Text.Json;
using System.Text.Json.Serialization;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Cli.Data;

public class GameMasterData
{
    public Dictionary<string, SkillMasterData> skills = new();
    public Dictionary<string, ClassMasterData> classes = new();
    public Dictionary<string, RaceMasterData> races = new();
    public Dictionary<string, EquipmentMasterData> equipment = new();
    public Dictionary<string, RelicMasterData> relics = new();
    public Dictionary<string, EnemyMasterData> enemies = new();
    public Dictionary<string, EnemyUnitTemplate> enemyUnits = new();
    public Dictionary<string, DungeonMasterData> dungeons = new();
    public List<QuestMasterData> allQuests = new();
    public List<AdventurerMasterData> allAdventurers = new();
}

public static class MasterLoader
{
    static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static GameMasterData Load(string dataDir)
    {
        var db = new GameMasterData();

        var skills = Load<List<SkillJson>>(dataDir, "skills.json");
        foreach (var s in skills)
        {
            var sd = new SkillMasterData
            {
                id = s.id, skillName = s.skillName, scope = s.scope,
                frontOnly = s.frontOnly, backOnly = s.backOnly,
                requireWeaponType = s.requireWeaponType, requiredWeaponType = s.requiredWeaponType,
                requireArmorType = s.requireArmorType, requiredArmorType = s.requiredArmorType,
                add = ParseStatBlock(s.add), mul = ParseMul(s.mul),
            };
            db.skills[s.id] = sd;
        }

        var classes = Load<List<ClassJson>>(dataDir, "classes.json");
        foreach (var c in classes)
        {
            var cd = new ClassMasterData
            {
                id = c.id, className = c.className,
                vitGrowth = c.vitGrowth, mentGrowth = c.mentGrowth, strGrowth = c.strGrowth,
                intGrowth = c.intGrowth, agiGrowth = c.agiGrowth,
            };
            foreach (var e in c.classSkills ?? new())
            {
                var entry = new ClassSkillEntry { skillId = e.skillId, requiredClearCount = e.requiredClearCount };
                if (db.skills.TryGetValue(e.skillId, out var sk)) entry.Skill = sk;
                cd.classSkills.Add(entry);
            }
            db.classes[c.id] = cd;
        }

        var races = Load<List<RaceJson>>(dataDir, "races.json");
        foreach (var r in races)
        {
            var rd = new RaceMasterData
            {
                id = r.id, raceName = r.raceName,
                vitGrowth = r.vitGrowth, mentGrowth = r.mentGrowth, strGrowth = r.strGrowth,
                intGrowth = r.intGrowth, agiGrowth = r.agiGrowth,
                allowedClassIds = r.allowedClassIds ?? new(),
            };
            db.races[r.id] = rd;
        }

        var equips = Load<List<EquipJson>>(dataDir, "equipment.json");
        foreach (var e in equips)
        {
            db.equipment[e.id] = new EquipmentMasterData
            {
                id = e.id, displayName = e.displayName, type = e.type,
                weaponType = e.weaponType, armorType = e.armorType,
                physicalCoeff = e.physicalCoeff, magicCoeff = e.magicCoeff, healCoeff = e.healCoeff,
                flatPhysicalAtk = e.flatPhysicalAtk, flatMagicAtk = e.flatMagicAtk, flatHeal = e.flatHeal,
                weight = e.weight, price = e.price, bonus = ParseStatBlock(e.bonus),
            };
        }

        var relics = Load<List<RelicJson>>(dataDir, "relics.json");
        foreach (var r in relics)
        {
            db.relics[r.id] = new RelicMasterData
            {
                id = r.id, relicName = r.relicName, description = r.description ?? "",
                effectType = r.effectType, rate = r.rate,
                add = ParseStatBlock(r.add), mul = ParseMul(r.mul),
            };
        }

        var enemies = Load<List<EnemyJson>>(dataDir, "enemies.json");
        foreach (var e in enemies)
        {
            var ed = new EnemyMasterData
            {
                id = e.id, baseName = e.baseName, exp = e.exp,
                vitality = e.vitality, mental = e.mental, strength = e.strength,
                agility = e.agility, intelligence = e.intelligence, constitution = e.constitution,
            };
            if (!string.IsNullOrEmpty(e.defaultWeaponId) && db.equipment.TryGetValue(e.defaultWeaponId, out var w)) ed.DefaultWeapon = w;
            if (!string.IsNullOrEmpty(e.defaultArmorId) && db.equipment.TryGetValue(e.defaultArmorId, out var a)) ed.DefaultArmor = a;
            foreach (var sid in e.skillIds ?? new())
                if (db.skills.TryGetValue(sid, out var sk)) ed.Skills.Add(sk);
            db.enemies[e.id] = ed;
        }

        var units = Load<List<EnemyUnitJson>>(dataDir, "enemy_units.json");
        foreach (var u in units)
        {
            var tpl = new EnemyUnitTemplate { id = u.id, unitName = u.unitName, baseLevel = u.baseLevel };
            foreach (var fid in u.formationIds ?? new())
            {
                EnemyMasterData? m = null;
                if (fid != null) db.enemies.TryGetValue(fid, out m);
                tpl.Formation.Add(m);
            }
            while (tpl.Formation.Count < 6) tpl.Formation.Add(null);
            db.enemyUnits[u.id] = tpl;
        }

        var dungeons = Load<List<DungeonJson>>(dataDir, "dungeons.json");
        foreach (var d in dungeons)
        {
            var dd = new DungeonMasterData
            {
                id = d.id, dungeonName = d.dungeonName,
                rewardChoiceMin = d.rewardChoiceMin, rewardChoiceMax = d.rewardChoiceMax,
                enemyLevelPerPhase = d.enemyLevelPerPhase,
            };
            foreach (var kv in d.eventTable ?? new())
                if (Enum.TryParse<DungeonEventType>(kv.Key, ignoreCase: true, out var et))
                    dd.eventTable[et] = kv.Value;
            foreach (var ec in d.encounterTable ?? new())
            {
                var entry = new EncounterEntry
                {
                    unitId = ec.unitId, weight = ec.weight,
                    minPhase = ec.minPhase <= 0 ? 1 : ec.minPhase,
                    maxPhase = ec.maxPhase,
                };
                if (db.enemyUnits.TryGetValue(ec.unitId, out var u)) entry.Unit = u;
                dd.encounterTable.Add(entry);
            }
            foreach (var re in d.rewardTable ?? new())
                dd.rewardTable.Add(ResolveRewardEntry(re, db));
            foreach (var re in d.treasureTable ?? new())
                dd.treasureTable.Add(ResolveRewardEntry(re, db));
            db.dungeons[d.id] = dd;
        }

        var quests = Load<List<QuestJson>>(dataDir, "quests.json");
        foreach (var q in quests)
        {
            var qd = new QuestMasterData
            {
                id = q.id, questName = q.questName, rank = q.rank, totalPhases = q.totalPhases,
                phasesPerTurn = q.phasesPerTurn > 0 ? q.phasesPerTurn : 5,
                rewardGold = q.rewardGold, rewardGuildPoints = q.rewardGuildPoints, rewardExp = q.rewardExp,
                isEmergencyQuest = q.isEmergencyQuest, rankUpOnClear = q.rankUpOnClear, requiredGuildPoints = q.requiredGuildPoints,
                bossPhase = q.bossPhase, bossDropsAreGuaranteed = q.bossDropsAreGuaranteed,
                gatherItemName = q.gatherItemName ?? "",
                gatherTargetCount = q.gatherTargetCount,
                gatherMinPerEvent = q.gatherMinPerEvent > 0 ? q.gatherMinPerEvent : 1,
                gatherMaxPerEvent = q.gatherMaxPerEvent > 0 ? q.gatherMaxPerEvent : 3,
                gatherChance = q.gatherChance > 0f ? q.gatherChance : 0.5f,
                gatherGoldPerItem = q.gatherGoldPerItem,
            };
            if (!string.IsNullOrEmpty(q.dungeonId) && db.dungeons.TryGetValue(q.dungeonId, out var dng)) qd.Dungeon = dng;
            if (!string.IsNullOrEmpty(q.bossEnemyId) && db.enemyUnits.TryGetValue(q.bossEnemyId, out var boss)) qd.BossEnemy = boss;
            foreach (var re in q.bossDrops ?? new()) qd.bossDrops.Add(ResolveRewardEntry(re, db));
            foreach (var fe in q.fixedEvents ?? new())
                qd.fixedEvents.Add(new QuestPhaseEvent { phase = fe.phase, type = (QuestEventType)fe.type });
            db.allQuests.Add(qd);
        }

        var advs = Load<List<AdvJson>>(dataDir, "adventurers.json");
        foreach (var a in advs)
        {
            int recruitGuildRank = Math.Max(1, a.recruitGuildRank ?? RecruitmentSystem.RequiredGuildRankForLevel(a.defaultLevel));
            int recruitWeight = Math.Max(0, a.recruitWeight ?? RecruitmentSystem.DefaultWeightForGuildRank(recruitGuildRank));
            var ad = new AdventurerMasterData
            {
                id = a.id, baseName = a.baseName, upkeepGold = a.upkeepGold,
                defaultLevel = a.defaultLevel, defaultRank = a.defaultRank,
                recruitGuildRank = recruitGuildRank,
                recruitWeight = recruitWeight,
                vitality = a.vitality, mental = a.mental, strength = a.strength,
                agility = a.agility, intelligence = a.intelligence,
                constitution = a.constitution, appearance = a.appearance,
                defaultClassId = a.defaultClassId ?? "", raceId = a.raceId ?? "",
                defaultWeaponId = a.defaultWeaponId ?? "", defaultArmorId = a.defaultArmorId ?? "",
                skillIds = a.skillIds ?? new(),
            };
            if (!string.IsNullOrEmpty(a.defaultClassId) && db.classes.TryGetValue(a.defaultClassId, out var cls)) ad.DefaultClass = cls;
            if (!string.IsNullOrEmpty(a.raceId) && db.races.TryGetValue(a.raceId, out var race)) ad.Race = race;
            if (!string.IsNullOrEmpty(a.defaultWeaponId) && db.equipment.TryGetValue(a.defaultWeaponId, out var wpn)) ad.DefaultWeapon = wpn;
            if (!string.IsNullOrEmpty(a.defaultArmorId) && db.equipment.TryGetValue(a.defaultArmorId, out var arm)) ad.DefaultArmor = arm;
            foreach (var sid in a.skillIds ?? new())
                if (db.skills.TryGetValue(sid, out var sk)) ad.Skills.Add(sk);
            db.allAdventurers.Add(ad);
        }

        return db;
    }

    static RewardEntryData ResolveRewardEntry(RewardEntryJson re, GameMasterData db)
    {
        var entry = new RewardEntryData { type = (RewardType)re.type, gold = re.gold, weight = re.weight, unique = re.unique };
        if (!string.IsNullOrEmpty(re.relicId) && db.relics.TryGetValue(re.relicId, out var rl)) entry.Relic = rl;
        if (!string.IsNullOrEmpty(re.equipmentId) && db.equipment.TryGetValue(re.equipmentId, out var eq)) entry.Equipment = eq;
        if (!string.IsNullOrEmpty(re.skillId) && db.skills.TryGetValue(re.skillId, out var sk2)) entry.Skill = sk2;
        return entry;
    }

    static StatBlock ParseStatBlock(Dictionary<string, int>? d)
    {
        if (d == null) return default;
        StatBlock b = default;
        d.TryGetValue("hp", out b.hp); d.TryGetValue("san", out b.san);
        d.TryGetValue("pAtk", out b.pAtk); d.TryGetValue("pDef", out b.pDef);
        d.TryGetValue("mAtk", out b.mAtk); d.TryGetValue("mDef", out b.mDef);
        d.TryGetValue("hit", out b.hit); d.TryGetValue("evade", out b.evade);
        d.TryGetValue("heal", out b.heal);
        return b;
    }

    static StatMultiplier ParseMul(Dictionary<string, float>? d)
    {
        var m = StatMultiplier.One;
        if (d == null) return m;
        if (d.TryGetValue("hp", out var v)) m.hp = v;
        if (d.TryGetValue("san", out v)) m.san = v;
        if (d.TryGetValue("pAtk", out v)) m.pAtk = v;
        if (d.TryGetValue("pDef", out v)) m.pDef = v;
        if (d.TryGetValue("mAtk", out v)) m.mAtk = v;
        if (d.TryGetValue("mDef", out v)) m.mDef = v;
        if (d.TryGetValue("hit", out v)) m.hit = v;
        if (d.TryGetValue("evade", out v)) m.evade = v;
        if (d.TryGetValue("heal", out v)) m.heal = v;
        return m;
    }

    static T Load<T>(string dir, string file)
    {
        var path = Path.Combine(dir, file);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, _opts)!;
    }

    // ---- DTO records ----
    record SkillJson(string id, string skillName, SkillScope scope,
        bool frontOnly, bool backOnly, bool requireWeaponType, WeaponType requiredWeaponType,
        bool requireArmorType, ArmorType requiredArmorType,
        Dictionary<string, int>? add, Dictionary<string, float>? mul);

    record ClassSkillEntryJson(string skillId, int requiredClearCount);
    record ClassJson(string id, string className, float vitGrowth, float mentGrowth, float strGrowth, float intGrowth, float agiGrowth, List<ClassSkillEntryJson>? classSkills);
    record RaceJson(string id, string raceName, float vitGrowth, float mentGrowth, float strGrowth, float intGrowth, float agiGrowth, List<string>? allowedClassIds);

    record EquipJson(string id, string displayName, EquipmentType type, WeaponType weaponType, ArmorType armorType,
        float physicalCoeff, float magicCoeff, float healCoeff, int flatPhysicalAtk, int flatMagicAtk, int flatHeal,
        int weight, int price, Dictionary<string, int>? bonus);

    record RelicJson(string id, string relicName, string? description, RelicEffectType effectType, float rate,
        Dictionary<string, int>? add, Dictionary<string, float>? mul);

    record EnemyJson(string id, string baseName, int exp, int vitality, int mental, int strength,
        int agility, int intelligence, int constitution, string? defaultWeaponId, string? defaultArmorId, List<string>? skillIds);

    record EnemyUnitJson(string id, string unitName, int baseLevel, List<string?>? formationIds);

    record RewardEntryJson(int type, string? relicId, string? equipmentId, string? skillId, int gold, int weight, bool unique);

    record EncounterEntryJson(string unitId, int weight, int minPhase, int maxPhase);
    record DungeonJson(string id, string dungeonName, int rewardChoiceMin, int rewardChoiceMax,
        Dictionary<string, int>? eventTable, List<EncounterEntryJson>? encounterTable,
        float enemyLevelPerPhase, List<RewardEntryJson>? rewardTable, List<RewardEntryJson>? treasureTable);

    record QuestPhaseEventJson(int phase, int type);
    record QuestJson(string id, string questName, int rank, int totalPhases, int phasesPerTurn,
        int rewardGold, int rewardGuildPoints,
        int rewardExp, bool isEmergencyQuest, int rankUpOnClear, int requiredGuildPoints, string? dungeonId, string? bossEnemyId,
        int bossPhase, bool bossDropsAreGuaranteed, List<RewardEntryJson>? bossDrops, List<QuestPhaseEventJson>? fixedEvents,
        string? gatherItemName, int gatherTargetCount, int gatherMinPerEvent, int gatherMaxPerEvent,
        float gatherChance, int gatherGoldPerItem);

    record AdvJson(string id, string baseName, int upkeepGold, int defaultLevel, int defaultRank,
        int? recruitGuildRank, int? recruitWeight,
        int vitality, int mental, int strength, int agility, int intelligence, int constitution, int appearance,
        string? defaultClassId, string? raceId, string? defaultWeaponId, string? defaultArmorId, List<string>? skillIds);
}

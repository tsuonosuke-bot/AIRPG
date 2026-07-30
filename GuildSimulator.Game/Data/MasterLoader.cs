using System.Text.Json;
using System.Text.Json.Serialization;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Game.Data;

public class GameMasterData
{
    public Dictionary<string, SkillMasterData> skills = new();
    public Dictionary<string, ClassMasterData> classes = new();
    public Dictionary<string, RaceMasterData> races = new();
    public Dictionary<string, EquipmentMasterData> equipment = new();
    public Dictionary<string, ConsumableMasterData> consumables = new();
    public Dictionary<string, RelicMasterData> relics = new();
    public Dictionary<string, FacilityMasterData> facilities = new();
    public Dictionary<string, EnemyMasterData> enemies = new();
    public Dictionary<string, EnemyUnitTemplate> enemyUnits = new();
    public Dictionary<string, DungeonMasterData> dungeons = new();
    public Dictionary<string, StoryClueMasterData> clues = new();
    public List<QuestMasterData> allQuests = new();
    public List<AdventurerMasterData> allAdventurers = new();
    public Dictionary<string, QuestChoiceEventMasterData> choiceEvents = new();
}

public static class MasterLoader
{
    static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>マスタJSONのファイル名一覧。読み込み順は <see cref="Load"/> 内の依存関係に従う。</summary>
    public static readonly IReadOnlyList<string> DataFileNames = new[]
    {
        "skills.json", "classes.json", "races.json", "equipment.json", "consumables.json",
        "relics.json", "facilities.json", "enemies.json", "choice_events.json", "enemy_units.json",
        "dungeons.json", "clues.json", "quests.json", "adventurers.json",
    };

    /// <summary>ディレクトリからマスタを読み込む（コンソール版）。</summary>
    public static GameMasterData Load(string dataDir) =>
        Load(file => File.ReadAllText(Path.Combine(dataDir, file)));

    /// <summary>
    /// ファイル名からJSON文字列を返す関数でマスタを読み込む。
    /// ブラウザ版のようにファイルシステムを持たないホストはこちらを使う。
    /// </summary>
    public static GameMasterData Load(Func<string, string> readJson)
    {
        var db = new GameMasterData();

        var skills = Load<List<SkillJson>>(readJson, "skills.json");
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

        var classes = Load<List<ClassJson>>(readJson, "classes.json");
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

        var races = Load<List<RaceJson>>(readJson, "races.json");
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

        var equips = Load<List<EquipJson>>(readJson, "equipment.json");
        foreach (var e in equips)
        {
            db.equipment[e.id] = new EquipmentMasterData
            {
                id = e.id, displayName = e.displayName, type = e.type,
                weaponType = e.weaponType, armorType = e.armorType,
                attackKind = e.attackKind, healPower = e.healPower, flatHeal = e.flatHeal,
                weight = e.weight, price = e.price, bonus = ParseStatBlock(e.bonus), rarity = e.rarity,
                shopTier = Math.Max(1, e.shopTier),
                damageDice = e.damageDice ?? "",
                basePv = e.basePv,
                maxStatBonus = Math.Max(0, e.maxStatBonus),
                armorPierce = Math.Max(0, e.armorPierce),
                armorShred = Math.Max(0, e.armorShred),
                critRange = Math.Clamp(e.critRange, 0, QudCombat.MAX_CRIT_RANGE),
                extraAttacks = Math.Max(0, e.extraAttacks),
                allowedSlots = e.allowedSlots ?? new(),
            };
        }

        var consumables = Load<List<ConsumableJson>>(readJson, "consumables.json");
        foreach (var c in consumables)
            db.consumables[c.id] = new ConsumableMasterData
            {
                id = c.id, displayName = c.displayName, description = c.description ?? "",
                rarity = c.rarity, price = c.price, effectType = c.effectType, effectValue = c.effectValue,
            };

        var relics = Load<List<RelicJson>>(readJson, "relics.json");
        foreach (var r in relics)
        {
            db.relics[r.id] = new RelicMasterData
            {
                id = r.id, relicName = r.relicName, description = r.description ?? "",
                effectType = r.effectType, rate = r.rate,
                add = ParseStatBlock(r.add), mul = ParseMul(r.mul),
            };
        }

        var facilities = Load<List<FacilityJson>>(readJson, "facilities.json");
        foreach (var f in facilities)
        {
            db.facilities[f.id] = new FacilityMasterData
            {
                id = f.id, displayName = f.displayName, description = f.description ?? "",
                buildCostGold = f.buildCostGold, upkeepGoldPerTurn = f.upkeepGoldPerTurn,
                requiredGuildRank = Math.Max(1, f.requiredGuildRank),
                questBoardBonus = f.questBoardBonus, shopLevelBonus = f.shopLevelBonus,
                restHealBonusPercent = f.restHealBonusPercent, growthRateBonusPercent = f.growthRateBonusPercent,
            };
        }

        var enemies = Load<List<EnemyJson>>(readJson, "enemies.json");
        foreach (var e in enemies)
        {
            var ed = new EnemyMasterData
            {
                id = e.id, baseName = e.baseName, exp = e.exp,
                vitality = e.vitality, mental = e.mental, strength = e.strength,
                agility = e.agility, intelligence = e.intelligence, constitution = e.constitution,
                naturalDamageDice = e.naturalDamageDice ?? "",
                naturalPv = e.naturalPv, naturalAv = e.naturalAv, naturalMav = e.naturalMav,
            };
            if (!string.IsNullOrEmpty(e.defaultWeaponId) && db.equipment.TryGetValue(e.defaultWeaponId, out var w)) ed.DefaultWeapon = w;
            if (!string.IsNullOrEmpty(e.defaultArmorId) && db.equipment.TryGetValue(e.defaultArmorId, out var a)) ed.DefaultArmor = a;
            foreach (var sid in e.skillIds ?? new())
                if (db.skills.TryGetValue(sid, out var sk)) ed.Skills.Add(sk);
            foreach (var drop in e.dropTable ?? new())
                ed.dropTable.Add(ResolveRewardEntry(drop, db));
            db.enemies[e.id] = ed;
        }

        var choiceEvents = Load<List<ChoiceEventJson>>(readJson, "choice_events.json");
        foreach (var ev in choiceEvents)
        {
            var master = new QuestChoiceEventMasterData
            {
                id = ev.id, title = ev.title, description = ev.description ?? "", weight = ev.weight,
            };
            foreach (var option in ev.options ?? new())
            {
                var resolvedOption = new QuestChoiceOptionData
                {
                    text = option.text, resultText = option.resultText ?? "",
                    effectType = option.effectType, value = option.value, targetId = option.targetId ?? "",
                };
                if (resolvedOption.effectType == QuestChoiceEffectType.Equipment
                    && db.equipment.TryGetValue(resolvedOption.targetId, out var choiceEquipment))
                    resolvedOption.Equipment = choiceEquipment;
                if (resolvedOption.effectType == QuestChoiceEffectType.Consumable
                    && db.consumables.TryGetValue(resolvedOption.targetId, out var choiceConsumable))
                    resolvedOption.Consumable = choiceConsumable;
                master.options.Add(resolvedOption);
            }
            db.choiceEvents[master.id] = master;
        }

        var units = Load<List<EnemyUnitJson>>(readJson, "enemy_units.json");
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

        var dungeons = Load<List<DungeonJson>>(readJson, "dungeons.json");
        foreach (var d in dungeons)
        {
            var dd = new DungeonMasterData
            {
                id = d.id, dungeonName = d.dungeonName,
                enemyLevelPerPhase = d.enemyLevelPerPhase,
                turnEndEventChance = d.turnEndEventChance ?? 0.35f,
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
            foreach (var re in d.treasureTable ?? new())
                dd.treasureTable.Add(ResolveRewardEntry(re, db));
            foreach (var eventId in d.turnEndEventIds ?? new())
                if (db.choiceEvents.TryGetValue(eventId, out var choiceEvent))
                    dd.turnEndEvents.Add(choiceEvent);
            db.dungeons[d.id] = dd;
        }

        var clues = Load<List<StoryClueJson>>(readJson, "clues.json");
        foreach (var clue in clues)
            db.clues[clue.id] = new StoryClueMasterData
            {
                id = clue.id,
                title = clue.title,
                description = clue.description ?? "",
            };

        var quests = Load<List<QuestJson>>(readJson, "quests.json");
        foreach (var q in quests)
        {
            var qd = new QuestMasterData
            {
                id = q.id, questName = q.questName,
                clientName = q.clientName ?? "", description = q.description ?? "",
                rank = q.rank, totalPhases = q.totalPhases,
                phasesPerTurn = q.phasesPerTurn > 0 ? q.phasesPerTurn : 5,
                rewardGold = q.rewardGold, rewardGuildPoints = q.rewardGuildPoints, rewardExp = q.rewardExp,
                isEmergencyQuest = q.isEmergencyQuest, rankUpOnClear = q.rankUpOnClear, requiredGuildPoints = q.requiredGuildPoints,
                bossPhase = q.bossPhase, bossDropsAreGuaranteed = q.bossDropsAreGuaranteed,
                isStoryQuest = q.isStoryQuest,
                requiredQuestIds = q.requiredQuestIds ?? new(),
                requiredClueIds = q.requiredClueIds ?? new(),
                grantedClueIds = q.grantedClueIds ?? new(),
                storyBranchId = q.storyBranchId ?? "",
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
            foreach (var clueId in qd.grantedClueIds)
                if (db.clues.TryGetValue(clueId, out var clue))
                    qd.GrantedClues.Add(clue);
            db.allQuests.Add(qd);
        }

        var advs = Load<List<AdvJson>>(readJson, "adventurers.json");
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
                rarity = a.rarity ?? DefaultAdventurerRarity(recruitWeight),
                vitality = a.vitality, mental = a.mental, strength = a.strength,
                agility = a.agility, intelligence = a.intelligence,
                constitution = a.constitution, appearance = a.appearance,
                defaultClassId = a.defaultClassId ?? "", raceId = a.raceId ?? "",
                defaultWeaponId = a.defaultWeaponId ?? "", defaultArmorId = a.defaultArmorId ?? "",
                skillIds = a.skillIds ?? new(),
                background = a.background ?? "",
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
        var entry = new RewardEntryData
        {
            type = (RewardType)re.type, gold = re.gold, weight = re.weight,
            chance = re.chance, quantity = re.quantity > 0 ? re.quantity : 1, unique = re.unique,
            relicId = re.relicId ?? "", equipmentId = re.equipmentId ?? "",
            skillId = re.skillId ?? "", consumableId = re.consumableId ?? "",
        };
        if (!string.IsNullOrEmpty(re.relicId) && db.relics.TryGetValue(re.relicId, out var rl)) entry.Relic = rl;
        if (!string.IsNullOrEmpty(re.equipmentId) && db.equipment.TryGetValue(re.equipmentId, out var eq)) entry.Equipment = eq;
        if (!string.IsNullOrEmpty(re.skillId) && db.skills.TryGetValue(re.skillId, out var sk2)) entry.Skill = sk2;
        if (!string.IsNullOrEmpty(re.consumableId) && db.consumables.TryGetValue(re.consumableId, out var item)) entry.Consumable = item;
        return entry;
    }

    static StatBlock ParseStatBlock(Dictionary<string, int>? d)
    {
        if (d == null) return default;
        StatBlock b = default;
        d.TryGetValue("hp", out b.hp); d.TryGetValue("san", out b.san);
        d.TryGetValue("av", out b.av); d.TryGetValue("mav", out b.mav);
        d.TryGetValue("pv", out b.pv); d.TryGetValue("mpv", out b.mpv);
        d.TryGetValue("dv", out b.dv); d.TryGetValue("toHit", out b.toHit);
        d.TryGetValue("heal", out b.heal);
        d.TryGetValue("armorPierce", out b.armorPierce);
        d.TryGetValue("armorShred", out b.armorShred);
        d.TryGetValue("critRange", out b.critRange);
        d.TryGetValue("extraAttacks", out b.extraAttacks);
        return b;
    }

    static StatMultiplier ParseMul(Dictionary<string, float>? d)
    {
        var m = StatMultiplier.One;
        if (d == null) return m;
        if (d.TryGetValue("hp", out var v)) m.hp = v;
        if (d.TryGetValue("san", out v)) m.san = v;
        if (d.TryGetValue("heal", out v)) m.heal = v;
        return m;
    }

    static T Load<T>(Func<string, string> readJson, string file) =>
        JsonSerializer.Deserialize<T>(readJson(file), _opts)!;

    static Rarity DefaultAdventurerRarity(int recruitWeight) => recruitWeight switch
    {
        <= 10 => Rarity.Legend,
        <= 25 => Rarity.Unique,
        <= 45 => Rarity.Rare,
        <= 75 => Rarity.Uncommon,
        _ => Rarity.Common,
    };

    // ---- DTO records ----
    record SkillJson(string id, string skillName, SkillScope scope,
        bool frontOnly, bool backOnly, bool requireWeaponType, WeaponType requiredWeaponType,
        bool requireArmorType, ArmorType requiredArmorType,
        Dictionary<string, int>? add, Dictionary<string, float>? mul);

    record ClassSkillEntryJson(string skillId, int requiredClearCount);
    record ClassJson(string id, string className, float vitGrowth, float mentGrowth, float strGrowth, float intGrowth, float agiGrowth, List<ClassSkillEntryJson>? classSkills);
    record RaceJson(string id, string raceName, float vitGrowth, float mentGrowth, float strGrowth, float intGrowth, float agiGrowth, List<string>? allowedClassIds);

    record EquipJson(string id, string displayName, EquipmentType type, WeaponType weaponType, ArmorType armorType,
        int weight, int price, Dictionary<string, int>? bonus, Rarity rarity, int shopTier = 1,
        AttackKind attackKind = AttackKind.Physical, float healPower = 0f, int flatHeal = 0,
        string? damageDice = null,
        int basePv = QudCombatDefaults.WeaponPv,
        int maxStatBonus = QudCombatDefaults.UnlimitedStatBonus,
        int armorPierce = 0, int armorShred = 0, int critRange = 0, int extraAttacks = 0,
        List<EquipSlot>? allowedSlots = null);

    record ConsumableJson(string id, string displayName, string? description, Rarity rarity,
        int price, ConsumableEffectType effectType, int effectValue);

    record RelicJson(string id, string relicName, string? description, RelicEffectType effectType, float rate,
        Dictionary<string, int>? add, Dictionary<string, float>? mul);

    record FacilityJson(string id, string displayName, string? description, int buildCostGold, int upkeepGoldPerTurn,
        int requiredGuildRank, int questBoardBonus, int shopLevelBonus, int restHealBonusPercent, int growthRateBonusPercent);

    record EnemyJson(string id, string baseName, int exp, int vitality, int mental, int strength,
        int agility, int intelligence, int constitution, string? defaultWeaponId, string? defaultArmorId,
        List<string>? skillIds, List<RewardEntryJson>? dropTable, string? naturalDamageDice = null,
        int naturalPv = QudCombatDefaults.WeaponPv, int naturalAv = 0, int naturalMav = 0);

    record EnemyUnitJson(string id, string unitName, int baseLevel, List<string?>? formationIds);

    record RewardEntryJson(int type, string? relicId, string? equipmentId, string? skillId,
        string? consumableId, int gold, int weight, float chance, int quantity, bool unique);

    record ChoiceOptionJson(string text, string? resultText, QuestChoiceEffectType effectType, int value, string? targetId);
    record ChoiceEventJson(string id, string title, string? description, int weight, List<ChoiceOptionJson>? options);

    record EncounterEntryJson(string unitId, int weight, int minPhase, int maxPhase);
    record DungeonJson(string id, string dungeonName,
        Dictionary<string, int>? eventTable, List<EncounterEntryJson>? encounterTable,
        float enemyLevelPerPhase, List<RewardEntryJson>? treasureTable,
        List<string>? turnEndEventIds, float? turnEndEventChance);

    record StoryClueJson(string id, string title, string? description);

    record QuestPhaseEventJson(int phase, int type);
    record QuestJson(string id, string questName, int rank, int totalPhases, int phasesPerTurn,
        int rewardGold, int rewardGuildPoints,
        int rewardExp, bool isEmergencyQuest, int rankUpOnClear, int requiredGuildPoints, string? dungeonId, string? bossEnemyId,
        int bossPhase, bool bossDropsAreGuaranteed, List<RewardEntryJson>? bossDrops, List<QuestPhaseEventJson>? fixedEvents,
        string? gatherItemName, int gatherTargetCount, int gatherMinPerEvent, int gatherMaxPerEvent,
        float gatherChance, int gatherGoldPerItem,
        string? clientName, string? description, bool isStoryQuest,
        List<string>? requiredQuestIds, List<string>? requiredClueIds, List<string>? grantedClueIds,
        string? storyBranchId);

    record AdvJson(string id, string baseName, int upkeepGold, int defaultLevel, int defaultRank,
        int? recruitGuildRank, int? recruitWeight,
        int vitality, int mental, int strength, int agility, int intelligence, int constitution, int appearance,
        string? defaultClassId, string? raceId, string? defaultWeaponId, string? defaultArmorId,
        List<string>? skillIds, Rarity? rarity,
        string? background);
}

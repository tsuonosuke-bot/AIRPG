using System.Text.Json;
using System.Text.Json.Serialization;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Game.Data;

public class GameMasterData
{
    public Dictionary<string, SkillMasterData> skills = new();

    /// <summary>遠征での戦い方から生える特性。効果の実体は <see cref="skills"/> のスキル。</summary>
    public Dictionary<string, TraitMasterData> traits = new();

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

    /// <summary>
    /// 読み込み時に解決できなかったID参照。<see cref="MasterLoader"/> は不明なIDを黙って読み飛ばすため、
    /// 打ち間違えると「エラーは出ないがゲーム内に一生出てこない」状態になる。
    /// ここに溜めておき、<see cref="MasterValidator"/> がエラーとして報告する。
    /// </summary>
    public List<string> unresolvedRefs = new();
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
        "skills.json", "traits.json", "classes.json", "races.json", "equipment.json", "consumables.json",
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
                family = s.family ?? "", level = s.level,
                frontOnly = s.frontOnly, backOnly = s.backOnly,
                requireWeaponType = s.requireWeaponType, requiredWeaponType = s.requiredWeaponType,
                requireArmorType = s.requireArmorType, requiredArmorType = s.requiredArmorType,
                requireUnarmed = s.requireUnarmed, requireTwoHanded = s.requireTwoHanded,
                requireShield = s.requireShield, requireOffHandWeapon = s.requireOffHandWeapon,
                requirePhysicalWeapon = s.requirePhysicalWeapon,
                unarmedDamageDice = s.unarmedDamageDice ?? "",
                add = ParseStatBlock(s.add), mul = ParseMul(s.mul),
                expedition = ParseExpedition(s.expedition),
                battle = ParseBattle(s.battle),
                battleStartStatuses = ParseCombatStatuses(s.battleStartStatuses),
                onHitStatuses = ParseCombatStatuses(s.onHitStatuses),
            };
            db.skills[s.id] = sd;
        }

        // 特性は効果の実体をスキルへ委ねているので、必ずスキルの後に読む。
        var traits = Load<List<TraitJson>>(readJson, "traits.json");
        foreach (var t in traits)
        {
            var td = new TraitMasterData
            {
                id = t.id, traitName = t.traitName, skillId = t.skillId,
                offerGroup = t.offerGroup ?? "",
                description = t.description ?? "",
                awakenText = t.awakenText ?? "",
                flavorText = t.flavorText ?? "",
            };
            if (db.skills.TryGetValue(t.skillId, out var traitSkill)) td.Skill = traitSkill;
            else Unresolved(db, "traits.json", t.id, "skillId", t.skillId);

            // 空なら全型。担い手の型ごとに意味が変わるので、絞るときだけ書く。
            foreach (var lens in t.builds ?? new())
                if (!td.builds.Contains(lens)) td.builds.Add(lens);

            foreach (var r in t.requirements ?? new())
                td.requirements.Add(new TraitRequirementData
                {
                    record = r.record,
                    atLeast = Math.Max(1, r.atLeast),
                });
            db.traits[t.id] = td;
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
                else Unresolved(db, "classes.json", c.id, "classSkills.skillId", e.skillId);
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
                offHandBonus = Math.Max(0, e.offHandBonus),
                isTwoHanded = e.isTwoHanded,
                blockChance = Math.Clamp(e.blockChance, 0, 100),
                blockAv = Math.Max(0, e.blockAv),
                allowedSlots = e.allowedSlots ?? new(),
                battleStartStatuses = ParseCombatStatuses(e.battleStartStatuses),
                onHitStatuses = ParseCombatStatuses(e.onHitStatuses),
            };
        }

        var consumables = Load<List<ConsumableJson>>(readJson, "consumables.json");
        foreach (var c in consumables)
            db.consumables[c.id] = new ConsumableMasterData
            {
                id = c.id, displayName = c.displayName, description = c.description ?? "",
                rarity = c.rarity, price = c.price, effectType = c.effectType, effectValue = c.effectValue,
                secondaryEffectValue = c.secondaryEffectValue,
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
                partySlotBonus = f.partySlotBonus,
                rosterSlotBonus = f.rosterSlotBonus,
                restHealBonusPercent = f.restHealBonusPercent, growthRateBonusPercent = f.growthRateBonusPercent,
                noviceQuestBoardBonus = Math.Max(0, f.noviceQuestBoardBonus),
                recruitMinBonus = f.recruitMinBonus,
                injuryRecoveryBonus = f.injuryRecoveryBonus,
                fatalityReductionPercent = Math.Clamp(f.fatalityReductionPercent, 0, 100),
                scarPreventionPercent = Math.Clamp(f.scarPreventionPercent, 0, 100),
            };
        }

        var enemies = Load<List<EnemyJson>>(readJson, "enemies.json");
        foreach (var e in enemies)
        {
            var ed = new EnemyMasterData
            {
                id = e.id, baseName = e.baseName, description = e.description ?? "", exp = e.exp,
                threat = Rank.Clamp(e.threat),
                vitality = e.vitality, mental = e.mental, strength = e.strength,
                agility = e.agility, intelligence = e.intelligence, constitution = e.constitution,
                naturalDamageDice = e.naturalDamageDice ?? "",
                naturalPv = e.naturalPv, naturalAv = e.naturalAv, naturalMav = e.naturalMav,
                naturalAttackKind = e.naturalAttackKind,
                defaultOffHandId = e.defaultOffHandId ?? "", defaultShieldId = e.defaultShieldId ?? "",
            };
            if (!string.IsNullOrEmpty(e.defaultWeaponId) && db.equipment.TryGetValue(e.defaultWeaponId, out var w)) ed.DefaultWeapon = w;
            if (!string.IsNullOrEmpty(e.defaultArmorId) && db.equipment.TryGetValue(e.defaultArmorId, out var a)) ed.DefaultArmor = a;
            if (!string.IsNullOrEmpty(e.defaultOffHandId) && db.equipment.TryGetValue(e.defaultOffHandId, out var oh)) ed.DefaultOffHand = oh;
            if (!string.IsNullOrEmpty(e.defaultShieldId) && db.equipment.TryGetValue(e.defaultShieldId, out var sh)) ed.DefaultShield = sh;
            foreach (var sid in e.skillIds ?? new())
                if (db.skills.TryGetValue(sid, out var sk)) ed.Skills.Add(sk);
                else Unresolved(db, "enemies.json", e.id, "skillIds", sid);
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
                    targetsOneMember = option.targetsOneMember,
                    grantedClueId = option.grantedClueId ?? "",
                    storyBranchId = option.storyBranchId ?? "",
                    storyOutcomeText = option.storyOutcomeText ?? "",
                };
                ResolveChoiceRefs(resolvedOption.effectType, resolvedOption.targetId, db,
                    out var optEquip, out var optItem, out var optSkill);
                resolvedOption.Equipment = optEquip;
                resolvedOption.Consumable = optItem;
                resolvedOption.Skill = optSkill;

                foreach (var oc in option.outcomes ?? new())
                {
                    var outcome = new QuestChoiceOutcome
                    {
                        weight = Math.Max(0, oc.weight), effectType = oc.effectType,
                        value = oc.value, targetId = oc.targetId ?? "",
                        resultText = oc.resultText ?? "",
                    };
                    ResolveChoiceRefs(outcome.effectType, outcome.targetId, db,
                        out var e2, out var c2, out var s2);
                    outcome.Equipment = e2;
                    outcome.Consumable = c2;
                    outcome.Skill = s2;
                    resolvedOption.outcomes.Add(outcome);
                }
                master.options.Add(resolvedOption);
            }
            db.choiceEvents[master.id] = master;
        }

        var units = Load<List<EnemyUnitJson>>(readJson, "enemy_units.json");
        foreach (var u in units)
        {
            var tpl = new EnemyUnitTemplate { id = u.id, unitName = u.unitName };
            foreach (var fid in u.formationIds ?? new())
            {
                EnemyMasterData? m = null;
                // 空文字は「その位置は空席」の意味なので数えない。打ち間違えたIDだけを拾う。
                if (!string.IsNullOrEmpty(fid) && !db.enemies.TryGetValue(fid, out m))
                    Unresolved(db, "enemy_units.json", u.id, "formationIds", fid);
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
                else Unresolved(db, "dungeons.json", d.id, "encounterTable.unitId", ec.unitId);
                dd.encounterTable.Add(entry);
            }
            foreach (var re in d.treasureTable ?? new())
                dd.treasureTable.Add(ResolveRewardEntry(re, db));
            foreach (var eventId in d.turnEndEventIds ?? new())
                if (db.choiceEvents.TryGetValue(eventId, out var choiceEvent))
                    dd.turnEndEvents.Add(choiceEvent);
                else Unresolved(db, "dungeons.json", d.id, "turnEndEventIds", eventId);
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

        // choice_events.json はダンジョン解決の都合で clues.json より先に読む。
        // 手掛かり参照だけは、手掛かりマスタが揃ったこの時点で解決する。
        foreach (var choiceEvent in db.choiceEvents.Values)
        foreach (var option in choiceEvent.options)
        {
            if (string.IsNullOrWhiteSpace(option.grantedClueId)) continue;
            if (db.clues.TryGetValue(option.grantedClueId, out var clue))
                option.GrantedClue = clue;
            else
                Unresolved(db, "choice_events.json", choiceEvent.id, "grantedClueId", option.grantedClueId);
        }

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
                storyArcId = q.storyArcId ?? "",
                storyArcTitle = q.storyArcTitle ?? "",
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
            if (!string.IsNullOrEmpty(q.dungeonId))
            {
                if (db.dungeons.TryGetValue(q.dungeonId, out var dng)) qd.Dungeon = dng;
                else Unresolved(db, "quests.json", q.id, "dungeonId", q.dungeonId);
            }
            if (!string.IsNullOrEmpty(q.bossEnemyId))
            {
                if (db.enemyUnits.TryGetValue(q.bossEnemyId, out var boss)) qd.BossEnemy = boss;
                else Unresolved(db, "quests.json", q.id, "bossEnemyId", q.bossEnemyId);
            }
            foreach (var re in q.bossDrops ?? new()) qd.bossDrops.Add(ResolveRewardEntry(re, db));
            foreach (var fe in q.fixedEvents ?? new())
            {
                var fixedEvent = new QuestPhaseEvent
                {
                    phase = fe.phase,
                    type = (QuestEventType)fe.type,
                    choiceEventId = fe.choiceEventId ?? "",
                };
                if (fixedEvent.type == QuestEventType.ForceChoice)
                {
                    if (db.choiceEvents.TryGetValue(fixedEvent.choiceEventId, out var choiceEvent))
                        fixedEvent.ChoiceEvent = choiceEvent;
                    else
                        Unresolved(db, "quests.json", q.id, "fixedEvents.choiceEventId", fixedEvent.choiceEventId);
                }
                qd.fixedEvents.Add(fixedEvent);
            }
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
                id = a.id, baseName = a.baseName,
                defaultLevel = a.defaultLevel, defaultRank = a.defaultRank,
                recruitGuildRank = recruitGuildRank,
                recruitWeight = recruitWeight,
                rarity = a.rarity ?? DefaultAdventurerRarity(recruitWeight),
                vitality = a.vitality, mental = a.mental, strength = a.strength,
                agility = a.agility, intelligence = a.intelligence,
                constitution = a.constitution, appearance = a.appearance,
                gender = a.gender ?? Gender.Unspecified,
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
                else Unresolved(db, "adventurers.json", a.id, "skillIds", sid);
            db.allAdventurers.Add(ad);
        }

        return db;
    }

    /// <summary>解決できなかったID参照を記録する。読み込み自体は続行し、報告は検証にまかせる。</summary>
    static void Unresolved(GameMasterData db, string file, string ownerId, string field, string value) =>
        db.unresolvedRefs.Add($"{file} の {ownerId}: {field} '{value}' が見つかりません");

    /// <summary>選択肢・結果の targetId が指す先を効果種別に応じて引き当てる。</summary>
    static void ResolveChoiceRefs(
        QuestChoiceEffectType type, string targetId, GameMasterData db,
        out EquipmentMasterData? equipment, out ConsumableMasterData? consumable, out SkillMasterData? skill)
    {
        equipment = null;
        consumable = null;
        skill = null;
        if (string.IsNullOrEmpty(targetId)) return;

        switch (type)
        {
            case QuestChoiceEffectType.Equipment:
                db.equipment.TryGetValue(targetId, out equipment);
                break;
            case QuestChoiceEffectType.Consumable:
                db.consumables.TryGetValue(targetId, out consumable);
                break;
            case QuestChoiceEffectType.AdventurerSkill:
                db.skills.TryGetValue(targetId, out skill);
                break;
            case QuestChoiceEffectType.Purchase:
                db.equipment.TryGetValue(targetId, out equipment);
                db.consumables.TryGetValue(targetId, out consumable);
                break;
        }
    }

    static RewardEntryData ResolveRewardEntry(RewardEntryJson re, GameMasterData db)
    {
        var entry = new RewardEntryData
        {
            type = (RewardType)re.type, gold = re.gold, weight = re.weight,
            chance = re.chance, quantity = re.quantity > 0 ? re.quantity : 1, unique = re.unique,
            minQuestRank = re.minQuestRank <= 0 ? Rank.Min : re.minQuestRank,
            maxQuestRank = re.maxQuestRank <= 0 ? Rank.Max : re.maxQuestRank,
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
        d.TryGetValue("offHandChance", out b.offHandChance);
        d.TryGetValue("blockChance", out b.blockChance);
        d.TryGetValue("blockNegate", out b.blockNegate);
        d.TryGetValue("carry", out b.carry);
        d.TryGetValue("threatWeight", out b.threatWeight);
        d.TryGetValue("autoPenetrate", out b.autoPenetrate);
        d.TryGetValue("critPv", out b.critPv);
        d.TryGetValue("emergencyHeal", out b.emergencyHeal);
        return b;
    }

    static SkillExpeditionEffect ParseExpedition(Dictionary<string, int>? d)
    {
        if (d == null) return default;
        SkillExpeditionEffect e = default;
        d.TryGetValue("goldPercent", out e.goldPercent);
        d.TryGetValue("expPercent", out e.expPercent);
        d.TryGetValue("treasureChancePercent", out e.treasureChancePercent);
        d.TryGetValue("trapChancePercent", out e.trapChancePercent);
        d.TryGetValue("enemyEncounterChancePercent", out e.enemyEncounterChancePercent);
        d.TryGetValue("healEventChancePercent", out e.healEventChancePercent);
        d.TryGetValue("restHealPercent", out e.restHealPercent);
        d.TryGetValue("enemyDropChancePercent", out e.enemyDropChancePercent);
        d.TryGetValue("rareDropChancePercent", out e.rareDropChancePercent);
        d.TryGetValue("postBattleHealPercent", out e.postBattleHealPercent);
        d.TryGetValue("postBattleHealPerCompanionPercent", out e.postBattleHealPerCompanionPercent);
        d.TryGetValue("phasesPerTurnBonus", out e.phasesPerTurnBonus);
        return e;
    }

    static SkillBattleEffect ParseBattle(Dictionary<string, int>? d)
    {
        if (d == null) return default;
        SkillBattleEffect e = default;
        d.TryGetValue("protectAllyHpPercent", out e.protectAllyHpPercent);
        d.TryGetValue("protectChancePercent", out e.protectChancePercent);
        d.TryGetValue("afflictedTargetPv", out e.afflictedTargetPv);
        d.TryGetValue("cleanseOnHealChancePercent", out e.cleanseOnHealChancePercent);
        d.TryGetValue("moraleOnHealPercent", out e.moraleOnHealPercent);
        d.TryGetValue("lowHpThresholdPercent", out e.lowHpThresholdPercent);
        d.TryGetValue("lowHpPv", out e.lowHpPv);
        d.TryGetValue("counterChancePercent", out e.counterChancePercent);
        return e;
    }

    static List<CombatStatusApplicationData> ParseCombatStatuses(List<CombatStatusJson>? values) =>
        (values ?? new())
        .Select(value => new CombatStatusApplicationData
        {
            type = value.type,
            target = value.target,
            chancePercent = Math.Clamp(value.chancePercent, 0, 100),
            durationRounds = Math.Max(1, value.durationRounds),
            potency = Math.Max(0, value.potency),
        })
        .ToList();

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
    record CombatStatusJson(CombatStatusType type, CombatStatusTarget target,
        int chancePercent = 100, int durationRounds = 2, int potency = 1);

    record SkillJson(string id, string skillName, SkillScope scope,
        bool frontOnly, bool backOnly, bool requireWeaponType, WeaponType requiredWeaponType,
        bool requireArmorType, ArmorType requiredArmorType,
        Dictionary<string, int>? add, Dictionary<string, float>? mul,
        string? family = null, int level = 0,
        bool requireUnarmed = false, bool requireTwoHanded = false,
        bool requireShield = false, bool requireOffHandWeapon = false,
        bool requirePhysicalWeapon = false,
        string? unarmedDamageDice = null,
        Dictionary<string, int>? expedition = null,
        Dictionary<string, int>? battle = null,
        List<CombatStatusJson>? battleStartStatuses = null,
        List<CombatStatusJson>? onHitStatuses = null);

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
        int offHandBonus = 0, bool isTwoHanded = false, int blockChance = 0, int blockAv = 0,
        List<EquipSlot>? allowedSlots = null,
        List<CombatStatusJson>? battleStartStatuses = null,
        List<CombatStatusJson>? onHitStatuses = null);

    record ConsumableJson(string id, string displayName, string? description, Rarity rarity,
        int price, ConsumableEffectType effectType, int effectValue, int secondaryEffectValue = 0);

    record RelicJson(string id, string relicName, string? description, RelicEffectType effectType, float rate,
        Dictionary<string, int>? add, Dictionary<string, float>? mul);

    record FacilityJson(string id, string displayName, string? description, int buildCostGold, int upkeepGoldPerTurn,
        int requiredGuildRank, int questBoardBonus, int shopLevelBonus, int restHealBonusPercent,
        int growthRateBonusPercent, int recruitMinBonus,
        int injuryRecoveryBonus = 0, int fatalityReductionPercent = 0, int scarPreventionPercent = 0,
        int noviceQuestBoardBonus = 0, int partySlotBonus = 0, int rosterSlotBonus = 0);

    record EnemyJson(string id, string baseName, int exp, int threat, int vitality, int mental, int strength,
        int agility, int intelligence, int constitution, string? defaultWeaponId, string? defaultArmorId,
        List<string>? skillIds, List<RewardEntryJson>? dropTable, string? naturalDamageDice = null,
        string? defaultOffHandId = null, string? defaultShieldId = null,
        int naturalPv = QudCombatDefaults.WeaponPv, int naturalAv = 0, int naturalMav = 0,
        AttackKind naturalAttackKind = AttackKind.Physical, string? description = null);

    record EnemyUnitJson(string id, string unitName, List<string?>? formationIds);

    record RewardEntryJson(int type, string? relicId, string? equipmentId, string? skillId,
        string? consumableId, int gold, int weight, float chance, int quantity, bool unique,
        int minQuestRank = Rank.Min, int maxQuestRank = Rank.Max);

    record ChoiceOutcomeJson(int weight, QuestChoiceEffectType effectType, int value,
        string? targetId, string? resultText);
    record ChoiceOptionJson(string text, string? resultText, QuestChoiceEffectType effectType, int value,
        string? targetId, bool targetsOneMember = false, List<ChoiceOutcomeJson>? outcomes = null,
        string? grantedClueId = null, string? storyBranchId = null, string? storyOutcomeText = null);
    record ChoiceEventJson(string id, string title, string? description, int weight, List<ChoiceOptionJson>? options);

    record EncounterEntryJson(string unitId, int weight, int minPhase, int maxPhase);
    record DungeonJson(string id, string dungeonName,
        Dictionary<string, int>? eventTable, List<EncounterEntryJson>? encounterTable,
        List<RewardEntryJson>? treasureTable,
        List<string>? turnEndEventIds, float? turnEndEventChance);

    record TraitRequirementJson(ExpeditionRecordType record, int atLeast);
    record TraitJson(string id, string traitName, string skillId, string? offerGroup,
        string? description, string? awakenText, string? flavorText,
        List<TraitRequirementJson>? requirements, List<TraitLens>? builds);

    record StoryClueJson(string id, string title, string? description);

    record QuestPhaseEventJson(int phase, int type, string? choiceEventId = null);
    record QuestJson(string id, string questName, int rank, int totalPhases, int phasesPerTurn,
        int rewardGold, int rewardGuildPoints,
        int rewardExp, bool isEmergencyQuest, int rankUpOnClear, int requiredGuildPoints, string? dungeonId, string? bossEnemyId,
        int bossPhase, bool bossDropsAreGuaranteed, List<RewardEntryJson>? bossDrops, List<QuestPhaseEventJson>? fixedEvents,
        string? gatherItemName, int gatherTargetCount, int gatherMinPerEvent, int gatherMaxPerEvent,
        float gatherChance, int gatherGoldPerItem,
        string? clientName, string? description, bool isStoryQuest,
        List<string>? requiredQuestIds, List<string>? requiredClueIds, List<string>? grantedClueIds,
        string? storyBranchId, string? storyArcId = null, string? storyArcTitle = null);

    record AdvJson(string id, string baseName, int defaultLevel, int defaultRank,
        int? recruitGuildRank, int? recruitWeight,
        int vitality, int mental, int strength, int agility, int intelligence, int constitution, int appearance,
        string? defaultClassId, string? raceId, string? defaultWeaponId, string? defaultArmorId,
        List<string>? skillIds, Rarity? rarity,
        string? background, Gender? gender);
}
